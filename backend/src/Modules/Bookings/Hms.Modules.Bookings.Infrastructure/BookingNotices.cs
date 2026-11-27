using Hms.Modules.Bookings.Contracts;
using Hms.Modules.Bookings.Domain;
using Hms.Modules.Hotel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Hms.Modules.Bookings.Infrastructure;

/// <summary>Брони для уведомлений и напоминаний гостю (модуль Messaging).</summary>
internal sealed class BookingNotices(DbContext db, IHotelContext hotel) : IBookingNotices
{
    public async Task<BookingNotice?> GetAsync(Guid bookingId, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var found = await QueryAsync(db.Set<Booking>().Where(b => b.HotelId == hotelId && b.Id == bookingId), ct);
        return found.Count > 0 ? found[0] : null;
    }

    public async Task<IReadOnlyList<BookingNotice>> StartingAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        return await QueryAsync(
            db.Set<Booking>().Where(b => b.HotelId == hotelId && b.Status == BookingStatus.Confirmed && b.StartsAt >= from && b.StartsAt < to), ct);
    }

    public async Task<IReadOnlyList<BookingNotice>> EndingAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        return await QueryAsync(
            db.Set<Booking>().Where(b => b.HotelId == hotelId && b.Status == BookingStatus.CheckedIn && b.EndsAt >= from && b.EndsAt < to), ct);
    }

    private static async Task<IReadOnlyList<BookingNotice>> QueryAsync(IQueryable<Booking> query, CancellationToken ct)
    {
        var rows = await query.AsNoTracking()
            .OrderBy(b => b.StartsAt)
            .Take(500)
            .Select(b => new { Booking = b, RoomId = b.Rooms.OrderByDescending(r => r.StartsAt).Select(r => r.RoomId).First() })
            .ToListAsync(ct);
        return rows.Select(r => new BookingNotice(
            r.Booking.Id, r.Booking.Number, r.Booking.Status, r.Booking.Kind.ToString(), r.Booking.GuestId, r.Booking.GuestName, r.RoomId,
            r.Booking.StartsAt, r.Booking.EndsAt, r.Booking.TotalAmount, r.Booking.PenaltyAmount, r.Booking.CancelReason, r.Booking.CreatedAt)).ToList();
    }
}
