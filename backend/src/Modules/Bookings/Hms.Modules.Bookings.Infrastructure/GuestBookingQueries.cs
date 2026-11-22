using Hms.Modules.Bookings.Contracts;
using Hms.Modules.Bookings.Domain;
using Hms.Modules.Rooms.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Hms.Modules.Bookings.Infrastructure;

/// <summary>История броней гостя, цифры для CRM и обезличивание (модуль Guests).</summary>
internal sealed class GuestBookingQueries(DbContext db, IRoomDirectory rooms, Stays stays) : IGuestBookingQueries
{
    private static readonly BookingStatus[] Counted = [BookingStatus.Pending, BookingStatus.AwaitingPayment, BookingStatus.Confirmed, BookingStatus.CheckedIn, BookingStatus.CheckedOut];
    private static readonly BookingStatus[] Visited = [BookingStatus.CheckedIn, BookingStatus.CheckedOut];

    public async Task<IReadOnlyList<GuestBookingItem>> ListAsync(Guid guestId, CancellationToken ct)
    {
        var rows = await db.Set<Booking>().AsNoTracking()
            .Where(b => b.GuestId == guestId)
            .OrderByDescending(b => b.StartsAt)
            .Select(b => new { b.Id, b.Number, b.Status, b.Kind, b.StartsAt, b.EndsAt, b.TotalAmount, b.CreatedAt, RoomId = b.Rooms.OrderByDescending(r => r.StartsAt).Select(r => r.RoomId).First() })
            .ToListAsync(ct);
        var roomInfo = await rooms.GetManyAsync(rows.Select(r => r.RoomId), ct);
        return rows.Select(b => new GuestBookingItem(b.Id, b.Number, b.Status, b.Kind.ToString(), stays.Format(b.StartsAt), stays.Format(b.EndsAt),
                roomInfo.GetValueOrDefault(b.RoomId)?.Number ?? "—", b.TotalAmount, b.CreatedAt))
            .ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, GuestBookingStats>> StatsAsync(IReadOnlyCollection<Guid> guestIds, CancellationToken ct)
    {
        if (guestIds.Count == 0)
        {
            return new Dictionary<Guid, GuestBookingStats>();
        }

        var rows = await db.Set<Booking>().AsNoTracking()
            .Where(b => b.GuestId != null && guestIds.Contains(b.GuestId.Value))
            .GroupBy(b => b.GuestId!.Value)
            .Select(g => new
            {
                GuestId = g.Key,
                Bookings = g.Count(),
                Visits = g.Count(b => Visited.Contains(b.Status)),
                Amount = g.Where(b => Counted.Contains(b.Status)).Sum(b => (decimal?)b.TotalAmount) ?? 0m,
                Last = g.Where(b => Counted.Contains(b.Status)).Max(b => (DateTimeOffset?)b.StartsAt),
            })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.GuestId, r => new GuestBookingStats(r.Bookings, r.Visits, r.Amount, r.Last));
    }

    public async Task AnonymizeAsync(Guid guestId, string placeholderName, CancellationToken ct)
    {
        // Снимок контакта в бронях.
        await db.Set<Booking>()
            .Where(b => b.GuestId == guestId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.GuestName, placeholderName)
                .SetProperty(b => b.GuestPhone, (string?)null)
                .SetProperty(b => b.GuestEmail, (string?)null), ct);

        // История изменений брони хранит старые и новые значения контакта — убираем эти поля из JSON.
        await db.Database.ExecuteSqlAsync(
            $"""
            UPDATE bookings.booking_history h
            SET old_values = h.old_values - 'guestName' - 'guestPhone' - 'guestEmail',
                new_values = h.new_values - 'guestName' - 'guestPhone' - 'guestEmail'
            FROM bookings.bookings b
            WHERE b.id = h.booking_id AND b.guest_id = {guestId}
            """,
            ct);
    }
}
