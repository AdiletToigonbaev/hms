using System.Globalization;
using Hms.Modules.Audit.Contracts;
using Hms.Modules.Bookings.Contracts;
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

/// <summary>Общее чтение для сервисов финансов: время гостиницы, имена сотрудников, брони, DTO.</summary>
internal sealed class FinanceReads(DbContext db, IHotelClock clock, IUserDirectory users, IBookingCharges bookings)
{
    public string Format(DateTimeOffset utc) => clock.ToHotelTime(utc).ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);

    public string? Format(DateTimeOffset? utc) => utc is { } v ? Format(v) : null;

    public Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IEnumerable<Guid> ids, CancellationToken ct) => users.GetNamesAsync(ids, ct);

    public async Task<List<PaymentListItemDto>> PaymentsAsync(IReadOnlyCollection<Payment> payments, CancellationToken ct)
    {
        var names = await users.GetNamesAsync(payments.Where(p => p.RegisteredBy != null).Select(p => p.RegisteredBy!.Value), ct);
        var charges = (await bookings.ListAsync(payments.Where(p => p.BookingId != null).Select(p => p.BookingId!.Value).Distinct().ToList(), ct))
            .ToDictionary(c => c.BookingId);
        return payments.Select(p =>
        {
            var charge = p.BookingId is { } id ? charges.GetValueOrDefault(id) : null;
            return new PaymentListItemDto(p.Id, p.Type.ToString(), p.Method.ToString(), p.Channel.ToString(), p.Amount, p.Status.ToString(), p.RefundedAmount,
                Format(p.PaidAt), p.BookingId, charge?.Number, charge?.GuestName,
                p.RegisteredBy is { } by ? names.GetValueOrDefault(by) : null, p.Comment, p.ReversalOfPaymentId);
        }).ToList();
    }

    public async Task<List<RefundDto>> RefundsAsync(IReadOnlyCollection<Refund> refunds, CancellationToken ct)
    {
        var ids = refunds.SelectMany(r => new[] { r.RequestedBy, r.DecidedBy, r.PaidBy }).Where(i => i != null).Select(i => i!.Value);
        var names = await users.GetNamesAsync(ids, ct);
        var charges = (await bookings.ListAsync(refunds.Where(r => r.BookingId != null).Select(r => r.BookingId!.Value).Distinct().ToList(), ct))
            .ToDictionary(c => c.BookingId);
        string? Name(Guid? id) => id is { } v ? names.GetValueOrDefault(v) : null;
        return refunds.Select(r => new RefundDto(r.Id, r.PaymentId, r.BookingId, r.BookingId is { } b ? charges.GetValueOrDefault(b)?.Number : null,
                r.Amount, r.Reason, r.Method.ToString(), r.Status.ToString(), Format(r.RequestedAt), Name(r.RequestedBy),
                Format(r.DecidedAt), Name(r.DecidedBy), r.DecisionComment, Format(r.PaidAt), Name(r.PaidBy), r.Version))
            .ToList();
    }

    /// <summary>Получено по броням: оплаты − сторно − выплаченные возвраты.</summary>
    public async Task<Dictionary<Guid, decimal>> ReceivedByBookingAsync(Guid hotelId, IReadOnlyCollection<Guid>? bookingIds, CancellationToken ct)
    {
        var query = db.Set<Payment>().AsNoTracking().Where(p => p.HotelId == hotelId && p.BookingId != null && ReceivedStatuses.Contains(p.Status));
        if (bookingIds is not null)
        {
            query = query.Where(p => bookingIds.Contains(p.BookingId!.Value));
        }

        return await query.GroupBy(p => p.BookingId!.Value)
            .Select(g => new { g.Key, Sum = g.Sum(p => p.Amount - p.RefundedAmount) })
            .ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
    }

    public static readonly PaymentStatus[] ReceivedStatuses =
        [PaymentStatus.Paid, PaymentStatus.RefundPending, PaymentStatus.PartiallyRefunded, PaymentStatus.Refunded, PaymentStatus.Reversed];
}

internal sealed class PaymentService(
    DbContext db,
    IHotelContext hotel,
    IBookingCharges bookings,
    FinanceReads reads,
    ICurrentUser currentUser,
    IUserAccessProvider access,
    IAuditTrail audit,
    IGuestNotifications notifications,
    IHotelClock clock) : IPaymentService
{
    public async Task<PaymentPageDto> ListAsync(PaymentQuery q, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var page = new PageRequest(q.Page, q.PageSize);
        var query = db.Set<Payment>().AsNoTracking().Where(p => p.HotelId == hotelId);
        if (q.From is { } from)
        {
            var fromUtc = clock.ToUtc(from, TimeOnly.MinValue);
            query = query.Where(p => p.PaidAt >= fromUtc);
        }

        if (q.To is { } to)
        {
            var toUtc = clock.ToUtc(to.AddDays(1), TimeOnly.MinValue);
            query = query.Where(p => p.PaidAt < toUtc);
        }

        if (Enum.TryParse<PaymentMethod>(q.Method, out var method))
        {
            query = query.Where(p => p.Method == method);
        }

        if (Enum.TryParse<PaymentType>(q.Type, out var type))
        {
            query = query.Where(p => p.Type == type);
        }

        if (q.BookingId is { } bookingId)
        {
            query = query.Where(p => p.BookingId == bookingId);
        }

        // Итоги «кассы» за выборку: поступления минус сторно и выплаченные возвраты, по способам оплаты.
        var byMethod = await query.Where(p => FinanceReads.ReceivedStatuses.Contains(p.Status))
            .GroupBy(p => p.Method)
            .Select(g => new { g.Key, Sum = g.Sum(p => p.Amount - p.RefundedAmount) })
            .ToListAsync(ct);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(p => p.PaidAt).Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        return new PaymentPageDto(
            new PagedResult<PaymentListItemDto>(await reads.PaymentsAsync(items, ct), page.Page, page.PageSize, total),
            new PaymentTotalsDto(byMethod.Sum(x => x.Sum), byMethod.ToDictionary(x => x.Key.ToString(), x => x.Sum), total));
    }

    public async Task<Result<PaymentDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var payment = await LoadAsync(id, tracking: false, ct);
        return payment is null ? FinanceErrors.NotFound : await ToDtoAsync(payment, ct);
    }

    public async Task<Result<PaymentDto>> RegisterAsync(RegisterPaymentRequest r, CancellationToken ct)
    {
        var type = Enum.Parse<PaymentType>(r.Type);
        var method = Enum.Parse<PaymentMethod>(r.Method);
        if (r.BookingId is null && type != PaymentType.Other)
        {
            return PaymentAppErrors.BookingRequired;
        }

        var hotelId = await hotel.GetHotelIdAsync(ct);
        BookingCharge? charge = null;

        // Платежи по одной брони — по очереди: два одновременных не превысят остаток к оплате.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (r.BookingId is { } bookingId)
        {
            charge = await bookings.GetAsync(bookingId, ct);
            if (charge is null)
            {
                return PaymentAppErrors.BookingNotFound;
            }

            await LockBookingAsync(bookingId, ct);
            var received = (await reads.ReceivedByBookingAsync(hotelId, [bookingId], ct)).GetValueOrDefault(bookingId);
            var balance = charge.Due - received;
            if (balance <= 0)
            {
                return PaymentAppErrors.NothingDue;
            }

            if (r.Amount > balance)
            {
                return PaymentAppErrors.Overpayment(balance);
            }
        }

        var created = Payment.RegisterManual(hotelId, r.BookingId, charge?.GuestId, type, method, r.Amount, currentUser.UserId, r.Comment, clock.UtcNow);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var payment = created.Value;
        db.Set<Payment>().Add(payment);
        audit.Record(new AuditEntry("payment.registered", "Payment", payment.Id.ToString(),
            After: new { Type = type.ToString(), Method = method.ToString(), payment.Amount, payment.BookingId, Booking = charge?.Number }));
        if (payment.BookingId is { } paidBooking && payment.GuestId is { } payer && type != PaymentType.Other)
        {
            notifications.Enqueue(new GuestNotification(NotificationEvent.PaymentReceived, payer, paidBooking, payment.Id, payment.Amount));
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await ToDtoAsync(payment, ct);
    }

    public async Task<Result<PaymentDto>> ReverseAsync(Guid id, ReversePaymentRequest r, CancellationToken ct)
    {
        var payment = await LoadAsync(id, tracking: true, ct);
        if (payment is null)
        {
            return FinanceErrors.NotFound;
        }

        var hasRefunds = await db.Set<Refund>().AnyAsync(x => x.PaymentId == id && x.Status != RefundStatus.Rejected, ct);
        var reversed = payment.Reverse(r.Reason, hasRefunds, currentUser.UserId, clock.UtcNow);
        if (reversed.IsFailure)
        {
            return reversed.Error;
        }

        db.Set<Payment>().Add(reversed.Value);
        audit.Record(new AuditEntry("payment.reversed", "Payment", id.ToString(), After: new { payment.Amount, Reason = r.Reason.Trim(), ReversalId = reversed.Value.Id }));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Параллельное сторно того же платежа: версия или уникальный индекс не дадут сторнировать дважды.
            db.ChangeTracker.Clear();
            return FinanceErrors.AlreadyReversed;
        }

        return await ToDtoAsync(payment, ct);
    }

    public async Task<Result<BookingBalanceDto>> GetBookingBalanceAsync(Guid bookingId, CancellationToken ct)
    {
        var charge = await bookings.GetAsync(bookingId, ct);
        if (charge is null)
        {
            return PaymentAppErrors.BookingNotFound;
        }

        var hotelId = await hotel.GetHotelIdAsync(ct);
        var payments = await db.Set<Payment>().AsNoTracking().Where(p => p.HotelId == hotelId && p.BookingId == bookingId).OrderBy(p => p.PaidAt).ToListAsync(ct);
        var refunds = await db.Set<Refund>().AsNoTracking().Where(x => x.BookingId == bookingId).OrderBy(x => x.RequestedAt).ToListAsync(ct);
        var received = payments.Where(p => p.IsReceived).Sum(p => p.Amount);
        var refunded = payments.Where(p => p.IsReceived).Sum(p => p.RefundedAmount);
        var settings = await hotel.GetSettingsAsync(ct);
        var upcoming = charge.Status is BookingStatus.Pending or BookingStatus.AwaitingPayment or BookingStatus.Confirmed;
        var prepayment = upcoming ? Math.Round(charge.Due * settings.PrepaymentPercent / 100m, 2, MidpointRounding.AwayFromZero) : 0m;
        return new BookingBalanceDto(bookingId, charge.Number, charge.Status.ToString(), charge.Due, received, refunded, charge.Due - (received - refunded), prepayment,
            await reads.PaymentsAsync(payments, ct), await reads.RefundsAsync(refunds, ct));
    }

    public async Task<DebtListDto> ListDebtsAsync(DebtQuery q, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var received = await reads.ReceivedByBookingAsync(hotelId, null, ct);
        var withDue = await bookings.ListWithDueAsync(ct);
        // Переплата бывает и без начисления (например, предоплата по отменённой без штрафа брони).
        var missing = received.Keys.Except(withDue.Select(c => c.BookingId)).ToList();
        var charges = missing.Count == 0 ? withDue : [.. withDue, .. await bookings.ListAsync(missing, ct)];
        var now = clock.UtcNow;
        var scope = q.Scope?.ToLowerInvariant() ?? "current";

        var items = charges
            .Select(c => (Charge: c, Received: received.GetValueOrDefault(c.BookingId)))
            .Select(x => (x.Charge, x.Received, Balance: x.Charge.Due - x.Received))
            .Where(x => scope switch
            {
                "overpaid" => x.Balance < 0,
                "upcoming" => x.Balance > 0 && x.Charge.StartsAt > now && x.Charge.Status is BookingStatus.Pending or BookingStatus.AwaitingPayment or BookingStatus.Confirmed,
                _ => x.Balance > 0 && (x.Charge.StartsAt <= now || x.Charge.Status is BookingStatus.CheckedIn or BookingStatus.CheckedOut or BookingStatus.Cancelled or BookingStatus.NoShow),
            })
            .OrderBy(x => x.Charge.StartsAt)
            .Select(x => new DebtDto(x.Charge.BookingId, x.Charge.Number, x.Charge.Status.ToString(), x.Charge.Kind, x.Charge.GuestName, x.Charge.RoomNumber,
                reads.Format(x.Charge.StartsAt), reads.Format(x.Charge.EndsAt), x.Charge.Due, x.Received, x.Balance))
            .ToList();
        return new DebtListDto(items, items.Sum(i => i.Balance));
    }

    private Task<int> LockBookingAsync(Guid bookingId, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({bookingId.ToString()}, 0))", ct);

    private async Task<Payment?> LoadAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var query = db.Set<Payment>().Include(p => p.Transactions).Where(p => p.Id == id && p.HotelId == hotelId);
        return await (tracking ? query : query.AsNoTracking()).FirstOrDefaultAsync(ct);
    }

    private async Task<PaymentDto> ToDtoAsync(Payment p, CancellationToken ct)
    {
        var item = (await reads.PaymentsAsync([p], ct))[0];
        var refunds = await db.Set<Refund>().AsNoTracking().Where(x => x.PaymentId == p.Id).OrderBy(x => x.RequestedAt).ToListAsync(ct);
        var byIds = p.Transactions.Where(t => t.CreatedBy != null).Select(t => t.CreatedBy!.Value);
        var names = await reads.NamesAsync(byIds, ct);
        var transactions = p.Transactions.OrderBy(t => t.OccurredAt)
            .Select(t => new PaymentTransactionDto(t.Id, t.Kind.ToString(), t.Amount, t.Status.ToString(), reads.Format(t.OccurredAt),
                t.CreatedBy is { } by ? names.GetValueOrDefault(by) : null, t.Comment))
            .ToList();

        var actions = new List<string>();
        var permissions = currentUser.UserId is { } userId ? (await access.GetAsync(userId, ct))?.Permissions : null;
        var activeRefunds = refunds.Any(x => x.IsActive);
        if (permissions?.Contains(Permissions.PaymentReverse) == true && p.Channel == PaymentChannel.Manual && p.Status == PaymentStatus.Paid && p.Type != PaymentType.Reversal && !activeRefunds)
        {
            actions.Add("reverse");
        }

        if (permissions?.Contains(Permissions.RefundRequest) == true && p.IsRefundable && p.Amount - refunds.Where(x => x.IsActive).Sum(x => x.Amount) > 0)
        {
            actions.Add("refund");
        }

        return new PaymentDto(item, transactions, await reads.RefundsAsync(refunds, ct), actions, p.Version);
    }
}
