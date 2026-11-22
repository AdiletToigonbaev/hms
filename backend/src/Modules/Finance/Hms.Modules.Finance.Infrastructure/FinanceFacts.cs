using Hms.Modules.Finance.Contracts;
using Hms.Modules.Finance.Domain;
using Hms.Modules.Hotel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Hms.Modules.Finance.Infrastructure;

/// <summary>Финансовые факты для модуля Reporting: только чтение, без персональных данных.</summary>
internal sealed class FinanceFacts(DbContext db, IHotelContext hotel) : IFinanceFacts
{
    public async Task<IReadOnlyList<PaymentFact>> PaymentsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        return await db.Set<Payment>().AsNoTracking()
            .Where(p => p.HotelId == hotelId && p.PaidAt >= from && p.PaidAt < to && FinanceReads.ReceivedStatuses.Contains(p.Status))
            .OrderBy(p => p.PaidAt)
            .Select(p => new PaymentFact(p.Id, p.BookingId, p.Type.ToString(), p.Method, p.Amount, p.Status.ToString(), p.PaidAt, p.RegisteredBy))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RefundFact>> RefundsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var paymentIds = db.Set<Payment>().Where(p => p.HotelId == hotelId).Select(p => p.Id);
        return await db.Set<Refund>().AsNoTracking()
            .Where(x => paymentIds.Contains(x.PaymentId)
                && ((x.RequestedAt >= from && x.RequestedAt < to) || (x.DecidedAt >= from && x.DecidedAt < to) || (x.PaidAt >= from && x.PaidAt < to)))
            .OrderBy(x => x.RequestedAt)
            .Select(x => new RefundFact(x.Id, x.PaymentId, x.BookingId, x.Amount, x.Method, x.Status.ToString(), x.Reason,
                x.RequestedAt, x.RequestedBy, x.DecidedAt, x.DecidedBy, x.PaidAt, x.PaidBy))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ExpenseFact>> ExpensesAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        return await db.Set<Expense>().AsNoTracking()
            .Where(e => e.HotelId == hotelId && !e.IsVoided && e.Date >= from && e.Date <= to)
            .Join(db.Set<ExpenseCategory>(), e => e.CategoryId, c => c.Id, (e, c) => new { e, c.Name })
            .OrderBy(x => x.e.Date)
            .Select(x => new ExpenseFact(x.e.Id, x.e.Date, x.Name, x.e.Amount, x.e.Method, x.e.Supplier, x.e.Description, x.e.CreatedBy))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, decimal>> NetReceivedAsync(IReadOnlyCollection<Guid> bookingIds, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        return await db.Set<Payment>().AsNoTracking()
            .Where(p => p.HotelId == hotelId && p.BookingId != null && bookingIds.Contains(p.BookingId.Value) && FinanceReads.ReceivedStatuses.Contains(p.Status))
            .GroupBy(p => p.BookingId!.Value)
            .Select(g => new { g.Key, Sum = g.Sum(p => p.Amount - p.RefundedAmount) })
            .ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
    }
}
