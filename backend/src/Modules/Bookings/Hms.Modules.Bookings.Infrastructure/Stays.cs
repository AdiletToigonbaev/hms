using System.Globalization;
using Hms.Modules.Bookings.Application;
using Hms.Modules.Bookings.Domain;
using Hms.Modules.Hotel.Contracts;
using Hms.Modules.Pricing.Contracts;
using Hms.Modules.Rooms.Contracts;
using Hms.Platform.Time;
using Hms.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Hms.Modules.Bookings.Infrastructure;

/// <summary>
/// Время проживания по правилам гостиницы: запрос (даты или дата + час + часы) → период в UTC с учётом уборки;
/// расчёт цены нужным способом (по ночам или по часам); поиск занятости номера.
/// </summary>
internal sealed class Stays(DbContext db, IHotelContext hotel, IHotelClock clock, IPriceCalculator prices)
{
    public const string LocalFormat = "yyyy-MM-dd'T'HH:mm";

    public async Task<Result<StayPeriod>> ResolveAsync(IStayInput input, CancellationToken ct)
    {
        var settings = await hotel.GetSettingsAsync(ct);
        if (!input.IsHourly())
        {
            var checkIn = input.CheckIn!.Value;
            var checkOut = input.CheckOut!.Value;
            if (checkIn < clock.Today)
            {
                return BookingErrors.PastDates;
            }

            return Nightly(settings, checkIn, checkOut);
        }

        var hours = input.Hours!.Value;
        if (hours < settings.HourlyMinHours || hours > settings.HourlyMaxHours)
        {
            return BookingErrors.HoursOutOfRange(settings.HourlyMinHours, settings.HourlyMaxHours);
        }

        var startLocal = input.Date!.Value.ToDateTime(new TimeOnly(input.StartHour!.Value, 0));
        var now = clock.LocalNow;
        var currentHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Unspecified);
        if (startLocal < currentHour)
        {
            return BookingErrors.StartInPast;
        }

        return Hourly(settings, startLocal, hours);
    }

    /// <summary>
    /// Посуточно: с часа заезда до часа выезда; уборка (по умолчанию гостиницы) — не дольше промежутка между выездом и заездом.
    /// Уборку конкретного номера уточняет <see cref="ForRoom"/>.
    /// </summary>
    public StayPeriod Nightly(HotelSettingsSnapshot settings, DateOnly checkIn, DateOnly checkOut)
    {
        var startsAt = clock.ToUtc(checkIn, settings.CheckInTime);
        var endsAt = clock.ToUtc(checkOut, settings.CheckOutTime);
        return new StayPeriod(BookingKind.Nightly, checkIn, checkOut, startsAt, endsAt, endsAt.AddMinutes(NightlyBuffer(settings, settings.CleaningBufferNightlyMinutes)),
            checkOut.DayNumber - checkIn.DayNumber);
    }

    /// <summary>
    /// Время уборки номера после брони: своё у категории (после почасовой / после посуточной) или из настроек гостиницы.
    /// Возвращает период с уточнённым концом занятости.
    /// </summary>
    public static StayPeriod ForRoom(StayPeriod stay, HotelSettingsSnapshot settings, RoomInfo room)
    {
        ArgumentNullException.ThrowIfNull(stay);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(room);
        var minutes = stay.Kind == BookingKind.Hourly
            ? room.CleaningHourlyMinutes ?? settings.CleaningBufferMinutes
            : NightlyBuffer(settings, room.CleaningNightlyMinutes ?? settings.CleaningBufferNightlyMinutes);
        return stay with { BlockedUntil = stay.EndsAt.AddMinutes(minutes) };
    }

    /// <summary>После посуточной брони уборка не дольше промежутка между часом выезда и часом заезда (иначе номер нельзя продать на следующую ночь).</summary>
    private static int NightlyBuffer(HotelSettingsSnapshot settings, int minutes)
    {
        var gap = Math.Max(0, (int)(settings.CheckInTime - settings.CheckOutTime).TotalMinutes);
        return Math.Min(minutes, gap);
    }

    /// <summary>Почасово: с начала на N часов, плюс уборка после.</summary>
    public StayPeriod Hourly(HotelSettingsSnapshot settings, DateTime startLocal, int hours)
    {
        var startsAt = clock.ToUtc(DateOnly.FromDateTime(startLocal), TimeOnly.FromDateTime(startLocal));
        var endsAt = startsAt.AddHours(hours);
        var endLocal = startLocal.AddHours(hours);
        return new StayPeriod(
            BookingKind.Hourly,
            DateOnly.FromDateTime(startLocal),
            DateOnly.FromDateTime(endLocal),
            startsAt,
            endsAt,
            endsAt.AddMinutes(settings.CleaningBufferMinutes),
            hours);
    }

    public Task<HotelSettingsSnapshot> SettingsAsync(CancellationToken ct) => hotel.GetSettingsAsync(ct);

    public DateTime Local(DateTimeOffset utc) => clock.ToHotelTime(utc).DateTime;

    public string Format(DateTimeOffset utc) => Local(utc).ToString(LocalFormat, CultureInfo.InvariantCulture);

    /// <summary>Цена: посуточно — по ночам, почасово — по часам (цена часа номера или категории).</summary>
    public Task<Result<PriceQuote>> PriceAsync(RoomInfo room, StayPeriod stay, string? promoCode, IReadOnlyList<ServiceLine> services, CancellationToken ct)
    {
        if (stay.Kind == BookingKind.Nightly)
        {
            return prices.QuoteAsync(
                new PriceRequest(room.RoomTypeId, room.BasePrice, room.PriceOverride, new DateRange(stay.CheckIn, stay.CheckOut), promoCode, services), ct);
        }

        var hourly = room.HourlyBasePrice ?? room.HourlyPriceOverride ?? 0m;
        return prices.QuoteHourlyAsync(
            new HourlyPriceRequest(room.RoomTypeId, hourly, room.HourlyPriceOverride, Local(stay.StartsAt), stay.Units, promoCode, services), ct);
    }

    /// <summary>Номера, занятые действующими бронями, пересекающимися с периодом (кроме брони <paramref name="except"/>).</summary>
    public async Task<HashSet<Guid>> BusyRoomsAsync(IReadOnlyCollection<Guid> roomIds, StayPeriod stay, Guid? except, CancellationToken ct) =>
        (await db.Set<BookingRoom>().AsNoTracking()
            .Where(b => b.IsBlocking && roomIds.Contains(b.RoomId) && b.BookingId != except && b.StartsAt < stay.BlockedUntil && b.BlockedUntil > stay.StartsAt)
            .Select(b => b.RoomId)
            .ToListAsync(ct))
        .ToHashSet();

    /// <summary>Ближайшая действующая бронь номера, начинающаяся не раньше момента <paramref name="after"/>.</summary>
    public Task<DateTimeOffset?> NextBookingStartAsync(Guid roomId, Guid except, DateTimeOffset after, CancellationToken ct) =>
        db.Set<BookingRoom>().AsNoTracking()
            .Where(b => b.IsBlocking && b.RoomId == roomId && b.BookingId != except && b.BlockedUntil > after)
            .OrderBy(b => b.StartsAt)
            .Select(b => (DateTimeOffset?)b.StartsAt)
            .FirstOrDefaultAsync(ct);
}
