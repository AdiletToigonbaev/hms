using System.Globalization;
using Hms.Modules.Audit.Contracts;
using Hms.Modules.Bookings.Contracts;
using Hms.Modules.Finance.Application;
using Hms.Modules.Finance.Domain;
using Hms.Modules.Guests.Contracts;
using Hms.Modules.Hotel.Contracts;
using Hms.Platform.Security;
using Hms.Platform.Time;
using Hms.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Hms.Modules.Finance.Infrastructure;

/// <summary>
/// Кабинет гостя (ТЗ §3.4): только свои платежи и возвраты — по броням гостя и платежам, записанным на его карточку.
/// Чужая бронь — 404, как несуществующая.
/// </summary>
internal sealed class MyFinanceService(
    DbContext db,
    IHotelContext hotel,
    ICurrentGuest currentGuest,
    IBookingCharges bookings,
    ICurrentUser currentUser,
    IAuditTrail audit,
    IHotelClock clock) : IMyFinanceService
{
    private static readonly Error BookingNotFound = Error.NotFound("booking.not_found", "Бронь не найдена.");

    public async Task<IReadOnlyList<MyPaymentDto>> PaymentsAsync(CancellationToken ct)
    {
        if (await ScopeAsync(ct) is not { } scope)
        {
            return [];
        }

        var payments = await db.Set<Payment>().AsNoTracking()
            .Where(p => p.HotelId == scope.HotelId && ((p.BookingId != null && scope.BookingIds.Contains(p.BookingId.Value)) || p.GuestId == scope.GuestId))
            .OrderByDescending(p => p.PaidAt).Take(200)
            .ToListAsync(ct);
        return payments.Select(p => ToDto(p, scope.Numbers)).ToList();
    }

    public async Task<IReadOnlyList<MyRefundDto>> RefundsAsync(CancellationToken ct)
    {
        if (await ScopeAsync(ct) is not { } scope)
        {
            return [];
        }

        var refunds = await db.Set<Refund>().AsNoTracking()
            .Where(x => x.BookingId != null && scope.BookingIds.Contains(x.BookingId.Value))
            .OrderByDescending(x => x.RequestedAt).Take(200)
            .ToListAsync(ct);
        return refunds.Select(x => ToDto(x, scope.Numbers)).ToList();
    }

    public async Task<Result<MyBookingBalanceDto>> BalanceAsync(Guid bookingId, CancellationToken ct)
    {
        if (await ScopeAsync(ct) is not { } scope || scope.Charges.FirstOrDefault(c => c.BookingId == bookingId) is not { } charge)
        {
            return BookingNotFound;
        }

        var payments = await db.Set<Payment>().AsNoTracking().Where(p => p.HotelId == scope.HotelId && p.BookingId == bookingId).OrderBy(p => p.PaidAt).ToListAsync(ct);
        var refunds = await db.Set<Refund>().AsNoTracking().Where(x => x.BookingId == bookingId).OrderBy(x => x.RequestedAt).ToListAsync(ct);
        var received = payments.Where(p => p.IsReceived).Sum(p => p.Amount);
        var refunded = payments.Where(p => p.IsReceived).Sum(p => p.RefundedAmount);
        var balance = charge.Due - (received - refunded);
        var settings = await hotel.GetSettingsAsync(ct);
        var upcoming = charge.Status is BookingStatus.Pending or BookingStatus.AwaitingPayment or BookingStatus.Confirmed;
        var prepayment = upcoming ? Math.Round(charge.Due * settings.PrepaymentPercent / 100m, 2, MidpointRounding.AwayFromZero) : 0m;
        return new MyBookingBalanceDto(bookingId, charge.Due, received, refunded, balance, prepayment, Refundable(balance, refunds),
            payments.Select(p => ToDto(p, scope.Numbers)).ToList(), refunds.Select(x => ToDto(x, scope.Numbers)).ToList());
    }

    /// <summary>
    /// Запрос возврата переплаты по своей брони. Сумма делится по платежам брони (сначала последние); способ — как у платежа.
    /// Решение и выплата — администратор (лимиты прав), как у запросов персонала.
    /// </summary>
    public async Task<Result<IReadOnlyList<MyRefundDto>>> RequestRefundAsync(MyRefundRequest r, CancellationToken ct)
    {
        if (await ScopeAsync(ct) is not { } scope || scope.Charges.FirstOrDefault(c => c.BookingId == r.BookingId) is not { } charge)
        {
            return BookingNotFound;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var payments = await db.Set<Payment>().AsNoTracking()
            .Where(p => p.HotelId == scope.HotelId && p.BookingId == r.BookingId)
            .OrderByDescending(p => p.PaidAt)
            .ToListAsync(ct);

        // Те же блокировки, что у запроса возврата персоналом: по платежам — по очереди, параллельные запросы не превысят суммы.
        foreach (var id in payments.Where(p => p.IsRefundable).Select(p => p.Id).Order())
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"refund:" + id}, 0))", ct);
        }

        var existing = await db.Set<Refund>().AsNoTracking().Where(x => x.BookingId == r.BookingId).ToListAsync(ct);
        var received = payments.Where(p => p.IsReceived).Sum(p => p.Amount);
        var refunded = payments.Where(p => p.IsReceived).Sum(p => p.RefundedAmount);
        var refundable = Refundable(charge.Due - (received - refunded), existing);
        if (r.Amount > refundable)
        {
            return refundable <= 0
                ? Error.BusinessRule("refund.nothing_to_refund", "По этой брони нет переплаты — возвращать нечего.")
                : Error.BusinessRule("refund.amount_exceeds_overpayment", $"Можно запросить не больше {refundable.ToString("0.##", CultureInfo.InvariantCulture)} сом.");
        }

        var left = r.Amount;
        var created = new List<Refund>();
        foreach (var payment in payments.Where(p => p.IsRefundable))
        {
            var reserved = existing.Where(x => x.PaymentId == payment.Id && x.Status != RefundStatus.Rejected).Sum(x => x.Amount);
            var part = Math.Min(left, payment.Amount - reserved);
            if (part <= 0)
            {
                continue;
            }

            var refund = Refund.Request(payment, part, $"Запрос гостя: {r.Reason.Trim()}", payment.Method, reserved, currentUser.UserId, clock.UtcNow);
            if (refund.IsFailure)
            {
                return refund.Error;
            }

            created.Add(refund.Value);
            db.Set<Refund>().Add(refund.Value);
            audit.Record(new AuditEntry("refund.requested_by_guest", "Refund", refund.Value.Id.ToString(),
                After: new { refund.Value.PaymentId, refund.Value.Amount, r.BookingId }));
            left -= part;
            if (left == 0)
            {
                break;
            }
        }

        if (left > 0)
        {
            db.ChangeTracker.Clear();
            return Error.BusinessRule("refund.amount_exceeds_overpayment", "Сумма больше, чем можно вернуть по платежам брони.");
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return created.Select(x => ToDto(x, scope.Numbers)).ToList();
    }

    /// <summary>Переплата минус возвраты, которые уже запрошены или одобрены, но ещё не выплачены.</summary>
    private static decimal Refundable(decimal balance, IEnumerable<Refund> refunds) =>
        Math.Max(0m, -balance - refunds.Where(x => x.Status is RefundStatus.Requested or RefundStatus.Approved).Sum(x => x.Amount));

    private async Task<GuestScope?> ScopeAsync(CancellationToken ct)
    {
        if (await currentGuest.GetAsync(ct) is not { } guest)
        {
            return null;
        }

        var charges = await bookings.ListForGuestAsync(guest.Id, ct);
        return new GuestScope(await hotel.GetHotelIdAsync(ct), guest.Id, charges,
            charges.Select(c => c.BookingId).ToList(), charges.ToDictionary(c => c.BookingId, c => c.Number));
    }

    private string Format(DateTimeOffset utc) => clock.ToHotelTime(utc).ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);

    private MyPaymentDto ToDto(Payment p, IReadOnlyDictionary<Guid, string> numbers) =>
        new(p.Id, p.BookingId, p.BookingId is { } b ? numbers.GetValueOrDefault(b) : null, p.Type.ToString(), p.Method.ToString(), p.Amount, p.RefundedAmount,
            p.Status.ToString(), Format(p.PaidAt));

    private MyRefundDto ToDto(Refund x, IReadOnlyDictionary<Guid, string> numbers) =>
        new(x.Id, x.BookingId, x.BookingId is { } b ? numbers.GetValueOrDefault(b) : null, x.Amount, x.Reason, x.Status.ToString(), Format(x.RequestedAt),
            x.DecidedAt is { } d ? Format(d) : null, x.DecisionComment, x.PaidAt is { } paid ? Format(paid) : null);

    private sealed record GuestScope(Guid HotelId, Guid GuestId, IReadOnlyList<BookingCharge> Charges, List<Guid> BookingIds, Dictionary<Guid, string> Numbers);
}
