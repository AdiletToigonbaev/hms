using System.Globalization;
using Hms.Modules.Bookings.Application;
using Hms.Modules.Bookings.Contracts;
using Hms.Modules.Bookings.Domain;
using Hms.Modules.Hotel.Contracts;
using Hms.Modules.Rooms.Contracts;
using Hms.Platform.Time;
using Hms.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Hms.Modules.Bookings.Infrastructure;

/// <summary>
/// Календарь занятости: все номера и брони, затрагивающие период (с уборкой после выезда).
/// Показываются действующие брони и завершённые (история); отменённые, истёкшие и неявки номер не занимают — их нет.
/// </summary>
internal sealed class CalendarService(DbContext db, IHotelContext hotel, IRoomDirectory rooms, IHotelClock clock, Stays stays) : ICalendarService
{
    public const int MaxDays = 31;

    public async Task<Result<CalendarDto>> GetAsync(CalendarQuery q, CancellationToken ct)
    {
        var from = q.From ?? clock.Today;
        var to = q.To ?? from;
        if (to < from || to.DayNumber - from.DayNumber + 1 > MaxDays)
        {
            return BookingErrors.CalendarRange;
        }

        var hotelId = await hotel.GetHotelIdAsync(ct);
        var settings = await hotel.GetSettingsAsync(ct);
        var fromUtc = clock.ToUtc(from, TimeOnly.MinValue);
        var toUtc = clock.ToUtc(to.AddDays(1), TimeOnly.MinValue);
        var now = clock.UtcNow;

        var rows = await (
            from r in db.Set<BookingRoom>().AsNoTracking()
            join b in db.Set<Booking>().AsNoTracking() on r.BookingId equals b.Id
            where b.HotelId == hotelId
                && (r.IsBlocking || b.Status == BookingStatus.CheckedOut)
                && r.StartsAt < toUtc && r.BlockedUntil > fromUtc
            orderby r.StartsAt
            select new { Booking = b, r.RoomId, r.StartsAt, r.EndsAt, r.BlockedUntil, ExtensionCount = b.Extensions.Count })
            .ToListAsync(ct);

        var roomList = await rooms.ListActiveAsync(ct);
        var bookings = rows.Select(x =>
        {
            var b = x.Booking;
            var noShowDue = b.Status == BookingStatus.Confirmed && now >= BookingRules.NoShowAt(settings, clock, b.Kind, b.StartsAt, b.CheckIn);
            return new CalendarBookingDto(
                b.Id, b.Number, x.RoomId, b.Status.ToString(), b.Kind.ToString(),
                stays.Format(x.StartsAt), stays.Format(x.EndsAt), stays.Format(x.BlockedUntil),
                b.GuestName, b.GuestPhone, b.Adults, b.Children, b.Hours, b.Nights, b.TotalAmount,
                b.HoldUntil is { } hold ? stays.Format(hold) : null,
                noShowDue,
                BookingStatusMachine.CanReschedule(b.Status) && x.ExtensionCount == 0,
                b.Version);
        }).ToList();

        return new CalendarDto(
            from,
            to,
            stays.Format(now),
            settings.CleaningBufferMinutes,
            settings.HourlyMinHours,
            settings.HourlyMaxHours,
            settings.CheckInTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            settings.CheckOutTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            roomList.Select(r => new CalendarRoomDto(r.Id, r.Number, r.Floor, r.RoomTypeId, r.RoomTypeName, r.Capacity, r.Status.ToString(), r.SellsHourly, r.IsSellable)).ToList(),
            bookings);
    }
}
