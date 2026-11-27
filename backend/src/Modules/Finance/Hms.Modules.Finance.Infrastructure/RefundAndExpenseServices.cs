using Hms.Modules.Audit.Contracts;
using Hms.Modules.Finance.Application;
using Hms.Modules.Finance.Contracts;
using Hms.Modules.Finance.Domain;
using Hms.Modules.Hotel.Contracts;
using Hms.Modules.Identity.Contracts;
using Hms.Modules.Messaging.Contracts;
using Hms.Platform.Security;
using Hms.Platform.Time;
using Hms.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Hms.Modules.Finance.Infrastructure;

/// <summary>
/// Возвраты: запрос (в пределах суммы платежа) → решение (в пределах лимита утверждающего) → выплата.
/// Статус платежа пересчитывается из возвратов в той же транзакции.
/// </summary>
internal sealed class RefundService(
    DbContext db,
    IHotelContext hotel,
    FinanceReads reads,
    ICurrentUser currentUser,
    IUserAccessProvider access,
    IPermissionLimits limits,
    IAuditTrail audit,
    IGuestNotifications notifications,
    IHotelClock clock) : IRefundService
{
    public async Task<RefundPageDto> ListAsync(RefundQuery q, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var page = new PageRequest(q.Page, q.PageSize);
        var query = db.Set<Refund>().AsNoTracking().Where(r => r.HotelId == hotelId);
        if (Enum.TryParse<RefundStatus>(q.Status, out var status))
        {
            query = query.Where(r => r.Status == status);
        }

        var total = await query.CountAsync(ct);
        // Сначала ждущие решения, затем ждущие выплаты, потом остальные — свежие выше.
        var items = await query
            .OrderBy(r => r.Status == RefundStatus.Requested ? 0 : r.Status == RefundStatus.Approved ? 1 : 2)
            .ThenByDescending(r => r.RequestedAt)
            .Skip(page.Skip).Take(page.PageSize)
            .ToListAsync(ct);
        var (canApprove, limit) = await ApproverAsync(ct);
        return new RefundPageDto(new PagedResult<RefundDto>(await reads.RefundsAsync(items, ct), page.Page, page.PageSize, total), canApprove, limit);
    }

    public async Task<Result<RefundDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var refund = await LoadAsync(id, tracking: false, ct);
        return refund is null ? FinanceErrors.RefundNotFound : (await reads.RefundsAsync([refund], ct))[0];
    }

    public async Task<Result<RefundDto>> RequestAsync(RequestRefundRequest r, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        // Запросы возврата по одному платежу — по очереди: два одновременных не превысят его сумму.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"refund:" + r.PaymentId}, 0))", ct);
        var payment = await db.Set<Payment>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == r.PaymentId && p.HotelId == hotelId, ct);
        if (payment is null)
        {
            return FinanceErrors.NotFound;
        }

        var reserved = await db.Set<Refund>().Where(x => x.PaymentId == payment.Id && x.Status != RefundStatus.Rejected).SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
        var created = Refund.Request(payment, r.Amount, r.Reason, Enum.Parse<PaymentMethod>(r.Method), reserved, currentUser.UserId, clock.UtcNow);
        if (created.IsFailure)
        {
            return created.Error;
        }

        db.Set<Refund>().Add(created.Value);
        audit.Record(new AuditEntry("refund.requested", "Refund", created.Value.Id.ToString(),
            After: new { created.Value.PaymentId, created.Value.Amount, Method = r.Method, Reason = created.Value.Reason }));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (await reads.RefundsAsync([created.Value], ct))[0];
    }

    public async Task<Result<RefundDto>> ApproveAsync(Guid id, DecideRefundRequest r, CancellationToken ct)
    {
        var (_, limit) = await ApproverAsync(ct);
        return await ChangeAsync(id, refund => refund.Approve(limit, currentUser.UserId, r.Comment, clock.UtcNow), "refund.approved", ct);
    }

    public Task<Result<RefundDto>> RejectAsync(Guid id, DecideRefundRequest r, CancellationToken ct) =>
        ChangeAsync(id, refund => refund.Reject(r.Comment ?? string.Empty, currentUser.UserId, clock.UtcNow), "refund.rejected", ct);

    public Task<Result<RefundDto>> MarkPaidAsync(Guid id, CancellationToken ct) =>
        ChangeAsync(id, refund => refund.MarkPaid(currentUser.UserId, clock.UtcNow), "refund.paid", ct);

    /// <summary>Изменение возврата и пересчёт статуса платежа — одной транзакцией; конкурентные решения отсекает версия.</summary>
    private async Task<Result<RefundDto>> ChangeAsync(Guid id, Func<Refund, Result> change, string action, CancellationToken ct)
    {
        var refund = await LoadAsync(id, tracking: true, ct);
        if (refund is null)
        {
            return FinanceErrors.RefundNotFound;
        }

        var result = change(refund);
        if (result.IsFailure)
        {
            return result.Error;
        }

        var payment = await db.Set<Payment>().Include(p => p.Transactions).FirstAsync(p => p.Id == refund.PaymentId, ct);
        var others = await db.Set<Refund>().AsNoTracking().Where(x => x.PaymentId == refund.PaymentId && x.Id != refund.Id).ToListAsync(ct);
        var all = others.Append(refund).ToList();
        payment.ApplyRefunds(
            all.Where(x => x.Status == RefundStatus.Approved).Sum(x => x.Amount),
            all.Where(x => x.Status == RefundStatus.Paid).Sum(x => x.Amount),
            currentUser.UserId,
            clock.UtcNow);
        audit.Record(new AuditEntry(action, "Refund", id.ToString(), After: new { refund.Amount, Status = refund.Status.ToString(), refund.DecisionComment }));
        if (refund.Status == RefundStatus.Paid && payment.GuestId is { } guestId)
        {
            notifications.Enqueue(new GuestNotification(NotificationEvent.RefundPaid, guestId, refund.BookingId, refund.Id, refund.Amount));
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return PaymentAppErrors.ConcurrencyConflict;
        }

        return (await reads.RefundsAsync([refund], ct))[0];
    }

    /// <summary>Может ли текущий сотрудник утверждать возвраты и до какой суммы (null — без лимита).</summary>
    private async Task<(bool CanApprove, decimal? Limit)> ApproverAsync(CancellationToken ct)
    {
        if (currentUser.UserId is not { } userId || await access.GetAsync(userId, ct) is not { } user || !user.Permissions.Contains(Permissions.RefundApprove))
        {
            return (false, null);
        }

        return (true, await limits.GetLimitAsync(userId, Permissions.RefundApprove, ct));
    }

    private async Task<Refund?> LoadAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var query = db.Set<Refund>().Where(x => x.Id == id && x.HotelId == hotelId);
        return await (tracking ? query : query.AsNoTracking()).FirstOrDefaultAsync(ct);
    }
}

/// <summary>
/// Расходы: сотрудник с <c>expense.create</c> вносит и видит свои; владелец (<c>expense.manage</c>) — все, правит и аннулирует.
/// </summary>
internal sealed class ExpenseService(
    DbContext db,
    IHotelContext hotel,
    IUserDirectory users,
    ICurrentUser currentUser,
    IUserAccessProvider access,
    IAuditTrail audit,
    TimeProvider time) : IExpenseService
{
    public async Task<ExpensePageDto> ListAsync(ExpenseQuery q, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var canManage = await CanManageAsync(ct);
        var page = new PageRequest(q.Page, q.PageSize);
        var query = db.Set<Expense>().AsNoTracking().Where(e => e.HotelId == hotelId);
        if (!canManage)
        {
            query = query.Where(e => e.CreatedBy == currentUser.UserId);
        }

        if (q.From is { } from)
        {
            query = query.Where(e => e.Date >= from);
        }

        if (q.To is { } to)
        {
            query = query.Where(e => e.Date <= to);
        }

        if (q.CategoryId is { } categoryId)
        {
            query = query.Where(e => e.CategoryId == categoryId);
        }

        var active = query.Where(e => !e.IsVoided);
        if (q.IncludeVoided != true)
        {
            query = active;
        }

        var categories = await db.Set<ExpenseCategory>().AsNoTracking().Where(c => c.HotelId == hotelId).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var byCategory = (await active.GroupBy(e => e.CategoryId).Select(g => new { g.Key, Sum = g.Sum(e => e.Amount) }).ToListAsync(ct))
            .ToDictionary(x => categories.GetValueOrDefault(x.Key) ?? "—", x => x.Sum);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedAt).Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        return new ExpensePageDto(new PagedResult<ExpenseDto>(await ToDtosAsync(items, categories, ct), page.Page, page.PageSize, total),
            byCategory.Values.Sum(), byCategory, canManage);
    }

    public async Task<Result<ExpenseDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var expense = await LoadAsync(id, tracking: false, ct);
        if (expense is null || (!await CanManageAsync(ct) && expense.CreatedBy != currentUser.UserId))
        {
            return FinanceErrors.ExpenseNotFound;
        }

        return await ToDtoAsync(expense, ct);
    }

    public async Task<Result<ExpenseDto>> CreateAsync(SaveExpenseRequest r, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var category = await CheckCategoryAsync(hotelId, r.CategoryId, null, ct);
        if (category is not null)
        {
            return category;
        }

        var created = Expense.Create(hotelId, r.CategoryId, r.Amount, r.Date, Enum.Parse<PaymentMethod>(r.Method), r.Supplier, r.Description, currentUser.UserId, time.GetUtcNow());
        if (created.IsFailure)
        {
            return created.Error;
        }

        db.Set<Expense>().Add(created.Value);
        audit.Record(new AuditEntry("expense.created", "Expense", created.Value.Id.ToString(), After: new { created.Value.Amount, created.Value.Date, r.CategoryId }));
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(created.Value, ct);
    }

    public async Task<Result<ExpenseDto>> UpdateAsync(Guid id, SaveExpenseRequest r, CancellationToken ct)
    {
        var expense = await LoadAsync(id, tracking: true, ct);
        if (expense is null)
        {
            return FinanceErrors.ExpenseNotFound;
        }

        var category = await CheckCategoryAsync(expense.HotelId, r.CategoryId, expense.CategoryId, ct);
        if (category is not null)
        {
            return category;
        }

        if (r.Version is { } version)
        {
            db.Entry(expense).Property(e => e.Version).OriginalValue = version;
        }

        var before = new { expense.Amount, expense.Date, expense.CategoryId, Method = expense.Method.ToString() };
        var result = expense.Update(r.CategoryId, r.Amount, r.Date, Enum.Parse<PaymentMethod>(r.Method), r.Supplier, r.Description, time.GetUtcNow());
        if (result.IsFailure)
        {
            return result.Error;
        }

        audit.Record(new AuditEntry("expense.updated", "Expense", id.ToString(), before, new { expense.Amount, expense.Date, expense.CategoryId, Method = r.Method }));
        return await SaveAsync(expense, ct);
    }

    public async Task<Result<ExpenseDto>> VoidAsync(Guid id, VoidExpenseRequest r, CancellationToken ct)
    {
        var expense = await LoadAsync(id, tracking: true, ct);
        if (expense is null)
        {
            return FinanceErrors.ExpenseNotFound;
        }

        var result = expense.Void(r.Reason, currentUser.UserId, time.GetUtcNow());
        if (result.IsFailure)
        {
            return result.Error;
        }

        audit.Record(new AuditEntry("expense.voided", "Expense", id.ToString(), After: new { expense.Amount, Reason = expense.VoidReason }));
        return await SaveAsync(expense, ct);
    }

    public async Task<IReadOnlyList<ExpenseCategoryDto>> ListCategoriesAsync(CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        return await db.Set<ExpenseCategory>().AsNoTracking().Where(c => c.HotelId == hotelId)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new ExpenseCategoryDto(c.Id, c.Name, c.SortOrder, c.IsActive))
            .ToListAsync(ct);
    }

    public async Task<Result<ExpenseCategoryDto>> SaveCategoryAsync(Guid? id, SaveExpenseCategoryRequest r, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var name = r.Name.Trim();
        if (await db.Set<ExpenseCategory>().AnyAsync(c => c.HotelId == hotelId && c.Id != id && c.Name == name, ct))
        {
            return Error.Conflict("expense.category_duplicate", "Категория с таким названием уже есть.");
        }

        ExpenseCategory category;
        if (id is { } existing)
        {
            var found = await db.Set<ExpenseCategory>().FirstOrDefaultAsync(c => c.Id == existing && c.HotelId == hotelId, ct);
            if (found is null)
            {
                return FinanceErrors.CategoryNotFound;
            }

            category = found;
            category.Rename(name, r.SortOrder);
        }
        else
        {
            category = new ExpenseCategory(hotelId, null, name, r.SortOrder);
            db.Set<ExpenseCategory>().Add(category);
        }

        audit.Record(new AuditEntry(id is null ? "expense.category.created" : "expense.category.updated", "ExpenseCategory", category.Id.ToString(), After: new { category.Name }));
        await db.SaveChangesAsync(ct);
        return new ExpenseCategoryDto(category.Id, category.Name, category.SortOrder, category.IsActive);
    }

    public async Task<Result<ExpenseCategoryDto>> SetCategoryActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var category = await db.Set<ExpenseCategory>().FirstOrDefaultAsync(c => c.Id == id && c.HotelId == hotelId, ct);
        if (category is null)
        {
            return FinanceErrors.CategoryNotFound;
        }

        category.SetActive(active);
        audit.Record(new AuditEntry(active ? "expense.category.activated" : "expense.category.deactivated", "ExpenseCategory", id.ToString()));
        await db.SaveChangesAsync(ct);
        return new ExpenseCategoryDto(category.Id, category.Name, category.SortOrder, category.IsActive);
    }

    /// <summary>Категория существует и включена (при правке можно оставить прежнюю, даже если её уже отключили).</summary>
    private async Task<Error?> CheckCategoryAsync(Guid hotelId, Guid categoryId, Guid? current, CancellationToken ct)
    {
        var category = await db.Set<ExpenseCategory>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == categoryId && c.HotelId == hotelId, ct);
        if (category is null)
        {
            return FinanceErrors.CategoryNotFound;
        }

        return !category.IsActive && category.Id != current ? FinanceErrors.CategoryInactive : null;
    }

    private async Task<bool> CanManageAsync(CancellationToken ct) =>
        currentUser.UserId is { } userId && await access.GetAsync(userId, ct) is { } user && user.Permissions.Contains(Permissions.ExpenseManage);

    private async Task<Expense?> LoadAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var query = db.Set<Expense>().Where(e => e.Id == id && e.HotelId == hotelId);
        return await (tracking ? query : query.AsNoTracking()).FirstOrDefaultAsync(ct);
    }

    private async Task<Result<ExpenseDto>> SaveAsync(Expense expense, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return PaymentAppErrors.ConcurrencyConflict;
        }

        return await ToDtoAsync(expense, ct);
    }

    private async Task<ExpenseDto> ToDtoAsync(Expense e, CancellationToken ct)
    {
        var categories = await db.Set<ExpenseCategory>().AsNoTracking().Where(c => c.Id == e.CategoryId).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        return (await ToDtosAsync([e], categories, ct))[0];
    }

    private async Task<List<ExpenseDto>> ToDtosAsync(IReadOnlyCollection<Expense> items, IReadOnlyDictionary<Guid, string> categories, CancellationToken ct)
    {
        var names = await users.GetNamesAsync(items.SelectMany(e => new[] { e.CreatedBy, e.VoidedBy }).Where(i => i != null).Select(i => i!.Value), ct);
        return items.Select(e => new ExpenseDto(e.Id, e.CategoryId, categories.GetValueOrDefault(e.CategoryId) ?? "—", e.Amount, e.Date, e.Method.ToString(),
                e.Supplier, e.Description, e.CreatedBy is { } c ? names.GetValueOrDefault(c) : null, e.CreatedAt, e.IsVoided, e.VoidReason,
                e.VoidedBy is { } v ? names.GetValueOrDefault(v) : null, e.Version))
            .ToList();
    }
}

/// <summary>Суммы оплат по гостям для модуля Guests (карточка гостя).</summary>
internal sealed class PaymentQueries(DbContext db, IHotelContext hotel) : IPaymentQueries
{
    public async Task<decimal> ReceivedForBookingAsync(Guid bookingId, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        return await db.Set<Payment>().AsNoTracking()
            .Where(p => p.HotelId == hotelId && p.BookingId == bookingId && FinanceReads.ReceivedStatuses.Contains(p.Status))
            .SumAsync(p => (decimal?)(p.Amount - p.RefundedAmount), ct) ?? 0m;
    }

    public async Task<IReadOnlyDictionary<Guid, decimal>> PaidByGuestsAsync(IReadOnlyCollection<Guid> guestIds, CancellationToken ct)
    {
        if (guestIds.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        var hotelId = await hotel.GetHotelIdAsync(ct);
        return await db.Set<Payment>().AsNoTracking()
            .Where(p => p.HotelId == hotelId && p.GuestId != null && guestIds.Contains(p.GuestId!.Value) && FinanceReads.ReceivedStatuses.Contains(p.Status))
            .GroupBy(p => p.GuestId!.Value)
            .Select(g => new { g.Key, Sum = g.Sum(p => p.Amount - p.RefundedAmount) })
            .ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
    }
}
