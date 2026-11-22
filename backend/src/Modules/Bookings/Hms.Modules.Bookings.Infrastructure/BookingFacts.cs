using System.Text.Json;
using Hms.Modules.Bookings.Contracts;
using Hms.Modules.Bookings.Domain;
using Hms.Modules.Hotel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Hms.Modules.Bookings.Infrastructure;

/// <summary>Брони для отчётов: одна выборка броней и одна — событий смены статуса (кто заселил, выселил, отменил).</summary>
internal sealed class BookingFacts(DbContext db, IHotelContext hotel) : IBookingFacts
{
    public async Task<IReadOnlyList<BookingFact>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var rows = await db.Set<Booking>().AsNoTracking()
            .Where(b => b.HotelId == hotelId && (
                (b.StartsAt < to && b.EndsAt > from)
                || (b.CreatedAt >= from && b.CreatedAt < to)
                || (b.CancelledAt >= from && b.CancelledAt < to)
                || (b.ActualCheckInAt >= from && b.ActualCheckInAt < to)
                || (b.ActualCheckOutAt >= from && b.ActualCheckOutAt < to)))
            .Select(b => new
            {
                Booking = b,
                Room = b.Rooms.Select(r => new { r.RoomId, r.RoomTypeId }).First(),
            })
            .ToListAsync(ct);

        var ids = rows.Select(r => r.Booking.Id).ToList();
        var events = await db.Set<BookingHistoryEntry>().AsNoTracking()
            .Where(h => ids.Contains(h.BookingId) && h.EventType == "status_changed")
            .Select(h => new { h.BookingId, h.NewValues, h.ChangedBy, h.ChangedAt })
            .ToListAsync(ct);
        // Последний сотрудник, переведший бронь в статус (по событию истории).
        var by = events
            .Select(e => (e.BookingId, Status: StatusOf(e.NewValues), e.ChangedBy, e.ChangedAt))
            .Where(e => e.Status is not null)
            .GroupBy(e => (e.BookingId, e.Status))
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.ChangedAt).Last().ChangedBy);
        Guid? Actor(Guid id, BookingStatus status) => by.GetValueOrDefault((id, status.ToString()));

        return rows.Select(r =>
        {
            var b = r.Booking;
            return new BookingFact(
                b.Id, b.Number, b.Kind.ToString(), b.Source.ToString(), b.Status, r.Room.RoomId, r.Room.RoomTypeId, b.GuestId,
                b.StartsAt, b.EndsAt, b.Hours, b.Nights,
                b.AccommodationAmount, b.DiscountAmount, b.ServicesAmount, b.ExtensionsAmount, b.EarlyDepartureCredit, b.TotalAmount, b.PenaltyAmount,
                b.CreatedAt, b.CreatedBy, b.CancelledAt, Actor(b.Id, BookingStatus.Cancelled),
                b.ActualCheckInAt, Actor(b.Id, BookingStatus.CheckedIn), b.ActualCheckOutAt, Actor(b.Id, BookingStatus.CheckedOut));
        }).ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, Guid>> RoomTypesAsync(IReadOnlyCollection<Guid> bookingIds, CancellationToken ct) =>
        await db.Set<BookingRoom>().AsNoTracking().Where(r => bookingIds.Contains(r.BookingId))
            .GroupBy(r => r.BookingId).Select(g => new { g.Key, Type = g.Select(r => r.RoomTypeId).First() })
            .ToDictionaryAsync(x => x.Key, x => x.Type, ct);

    public async Task<int> PendingCountAsync(DateTimeOffset now, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        return await db.Set<Booking>().CountAsync(b => b.HotelId == hotelId && (b.Status == BookingStatus.Pending || b.Status == BookingStatus.AwaitingPayment) && b.EndsAt > now, ct);
    }

    private static string? StatusOf(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("status", out var s) || doc.RootElement.TryGetProperty("Status", out s) ? s.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
