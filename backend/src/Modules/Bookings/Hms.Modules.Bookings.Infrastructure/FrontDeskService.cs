using Hms.Modules.Audit.Contracts;
using Hms.Modules.Bookings.Application;
using Hms.Modules.Bookings.Contracts;
using Hms.Modules.Bookings.Domain;
using Hms.Modules.Finance.Contracts;
using Hms.Modules.Hotel.Contracts;
using Hms.Modules.Housekeeping.Contracts;
using Hms.Modules.Rooms.Contracts;
using Hms.Platform.Security;
using Hms.Platform.Time;
using Hms.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Hms.Modules.Bookings.Infrastructure;

/// <summary>
/// Стойка: заселение и выселение (ТЗ §3.2, много раз в день при почасовых бронях).
/// Заселение: бронь «Гость заселён», номер «Занят». Выселение: бронь «Гость выехал», номер «Уборка»,
/// задача уборки со сроком = время уборки номера. Всё — одной транзакцией (один контекст БД).
/// </summary>
internal sealed class FrontDeskService(
    DbContext db,
    IHotelContext hotel,
    IRoomDirectory rooms,
    IRoomStatusChanger roomStatus,
    IHousekeepingTasks housekeeping,
    IPaymentQueries payments,
    IBookingService bookings,
    Stays stays,
    ICurrentUser currentUser,
    IAuditTrail audit,
    IHotelClock clock) : IFrontDeskService
{
    /// <summary>Почасовую бронь можно заселить за час до начала (гость пришёл раньше).</summary>
    public static readonly TimeSpan HourlyEarlyCheckIn = TimeSpan.FromMinutes(60);

    public async Task<Result<CheckInPreviewDto>> CheckInPreviewAsync(Guid bookingId, CancellationToken ct)
    {
        var booking = await LoadAsync(bookingId, tracking: false, ct);
        if (booking is null)
        {
            return BookingErrors.NotFound;
        }

        var room = await rooms.GetAsync(booking.Rooms[0].RoomId, ct);
        var block = await CheckInBlockAsync(booking, room, ct);
        var received = await payments.ReceivedForBookingAsync(booking.Id, ct);
        return new CheckInPreviewDto(block is null, block?.Code, block?.Message, room?.Number ?? "—", room?.Status.ToString() ?? "—",
            clock.UtcNow < booking.StartsAt, stays.Format(booking.StartsAt), stays.Format(booking.EndsAt),
            booking.TotalAmount, received, booking.TotalAmount - received);
    }

    public async Task<Result<BookingDto>> CheckInAsync(Guid bookingId, CancellationToken ct)
    {
        var booking = await LoadAsync(bookingId, tracking: true, ct);
        if (booking is null)
        {
            return BookingErrors.NotFound;
        }

        var room = await rooms.GetAsync(booking.Rooms[0].RoomId, ct);
        if (await CheckInBlockAsync(booking, room, ct) is { } block)
        {
            return block;
        }

        var now = clock.UtcNow;
        var result = booking.CheckInGuest(currentUser.UserId, now);
        if (result.IsFailure)
        {
            return result.Error;
        }

        var occupied = await roomStatus.ChangeAsync(room!.Id, RoomStatus.Occupied, RoomStatusSource.CheckIn, null, booking.Id, ct);
        if (occupied.IsFailure)
        {
            db.ChangeTracker.Clear();
            return occupied.Error;
        }

        audit.Record(new AuditEntry("booking.checked_in", "Booking", booking.Id.ToString(), After: new { booking.Number, room = room.Number, Early = now < booking.StartsAt }));
        return await SaveAsync(booking.Id, ct);
    }

    public async Task<Result<CheckOutPreviewDto>> CheckOutPreviewAsync(Guid bookingId, CancellationToken ct)
    {
        var booking = await LoadAsync(bookingId, tracking: false, ct);
        if (booking is null)
        {
            return BookingErrors.NotFound;
        }

        if (booking.Status != BookingStatus.CheckedIn)
        {
            return NotCheckedIn;
        }

        var plan = await PlanCheckOutAsync(booking, ct);
        var received = await payments.ReceivedForBookingAsync(booking.Id, ct);
        var totalAfter = booking.TotalAmount - plan.Credit;
        return new CheckOutPreviewDto(plan.Early, plan.LateMinutes, plan.UnusedUnits, booking.Kind == BookingKind.Hourly ? "Hour" : "Night",
            plan.Credit, booking.TotalAmount, totalAfter, received, totalAfter - received, stays.Format(booking.EndsAt), stays.Format(plan.CleaningDueAt));
    }

    public async Task<Result<BookingDto>> CheckOutAsync(Guid bookingId, CheckOutRequest r, CancellationToken ct)
    {
        var booking = await LoadAsync(bookingId, tracking: true, ct);
        if (booking is null)
        {
            return BookingErrors.NotFound;
        }

        if (booking.Status != BookingStatus.CheckedIn)
        {
            return NotCheckedIn;
        }

        var plan = await PlanCheckOutAsync(booking, ct);
        var received = await payments.ReceivedForBookingAsync(booking.Id, ct);
        var debt = booking.TotalAmount - plan.Credit - received;
        if (debt > 0 && !r.AllowDebt)
        {
            return Error.BusinessRule("checkout.debt", $"Гость не оплатил {debt:0.##} сом. Примите оплату или подтвердите выселение с долгом.");
        }

        var result = booking.CheckOutGuest(currentUser.UserId, clock.UtcNow, plan.Shortened, plan.Credit);
        if (result.IsFailure)
        {
            return result.Error;
        }

        var roomId = booking.Rooms[0].RoomId;
        var cleaning = await roomStatus.ChangeAsync(roomId, RoomStatus.Cleaning, RoomStatusSource.CheckOut, null, booking.Id, ct);
        if (cleaning.IsFailure)
        {
            db.ChangeTracker.Clear();
            return cleaning.Error;
        }

        await housekeeping.CreateAfterCheckOutAsync(roomId, booking.Id, plan.CleaningDueAt, ct);
        audit.Record(new AuditEntry("booking.checked_out", "Booking", booking.Id.ToString(),
            After: new { booking.Number, plan.Early, plan.Credit, Debt = Math.Max(0, debt) }));
        return await SaveAsync(booking.Id, ct);
    }

    private static readonly Error NotCheckedIn = Error.Conflict("booking.not_checked_in", "Гость не заселён — выселять некого.");

    /// <summary>
    /// Досрочный выезд: время брони сокращается до сейчас. Перерасчёт по настройкам владельца:
    /// почасовая — неиспользованные целые часы возвращаются, только если включено «Возвращать неиспользованные часы»;
    /// посуточная — оплачиваются прожитые ночи (минимум одна) плюс штраф за досрочный выезд (по умолчанию 0 ночей).
    /// Срок уборки — время уборки номера от момента выезда.
    /// </summary>
    private async Task<CheckOutPlan> PlanCheckOutAsync(Booking booking, CancellationToken ct)
    {
        var settings = await stays.SettingsAsync(ct);
        var now = clock.UtcNow;
        var room = await rooms.GetAsync(booking.Rooms[0].RoomId, ct);
        var lateMinutes = now > booking.EndsAt ? (int)(now - booking.EndsAt).TotalMinutes : 0;

        StayPeriod? shortened = null;
        var early = false;
        var unused = 0;
        var creditUnits = 0;
        if (now < booking.EndsAt - TimeSpan.FromMinutes(5))
        {
            var today = clock.Today;
            early = true;
            // Заселили раньше начала и выехали до него — время брони не сокращаем (период не может быть пустым); номер освобождается выселением.
            if (now > booking.StartsAt)
            {
                shortened = new StayPeriod(booking.Kind, booking.CheckIn, today < booking.CheckOut ? today : booking.CheckOut, booking.StartsAt, now, now, booking.Units);
            }
            if (booking.Kind == BookingKind.Hourly)
            {
                unused = Math.Min(booking.Units, (int)Math.Floor((booking.EndsAt - (now < booking.StartsAt ? booking.StartsAt : now)).TotalHours));
                creditUnits = settings.HourlyPolicy.RefundUnusedOnEarlyDeparture ? unused : 0;
            }
            else
            {
                var usedNights = Math.Max(1, today.DayNumber - booking.CheckIn.DayNumber);
                unused = Math.Max(0, booking.Nights - usedNights);
                creditUnits = Math.Max(0, unused - settings.CancellationPolicy.EarlyDeparturePenaltyNights);
            }
        }

        var credit = BookingPenalties.Amount(booking, creditUnits);
        var cleaningFrom = now;
        var cleaningDue = room is null ? cleaningFrom : Stays.ForRoom(new StayPeriod(booking.Kind, booking.CheckIn, booking.CheckOut, booking.StartsAt, cleaningFrom, cleaningFrom, booking.Units), settings, room).BlockedUntil;
        if (shortened is not null)
        {
            shortened = shortened with { BlockedUntil = cleaningDue < shortened.EndsAt ? shortened.EndsAt : cleaningDue };
        }

        return new CheckOutPlan(early, shortened, unused, credit, lateMinutes, cleaningDue < now ? now : cleaningDue);
    }

    /// <summary>Почему нельзя заселить сейчас (null — можно).</summary>
    private async Task<Error?> CheckInBlockAsync(Booking booking, RoomInfo? room, CancellationToken ct)
    {
        if (booking.Status is BookingStatus.Pending or BookingStatus.AwaitingPayment)
        {
            return Error.BusinessRule("booking.checkin_not_confirmed", "Сначала подтвердите бронь.");
        }

        if (booking.Status != BookingStatus.Confirmed)
        {
            return Error.Conflict("booking.invalid_transition", "Заселить можно только подтверждённую бронь.");
        }

        var now = clock.UtcNow;
        if (now >= booking.EndsAt)
        {
            return Error.BusinessRule("booking.checkin_too_late", "Время брони уже закончилось — заселить нельзя. Создайте новую бронь.");
        }

        var tooEarly = booking.Kind == BookingKind.Hourly ? now < booking.StartsAt - HourlyEarlyCheckIn : clock.Today < booking.CheckIn;
        if (tooEarly)
        {
            return Error.BusinessRule("booking.checkin_too_early", booking.Kind == BookingKind.Hourly
                ? "Заселить можно не раньше чем за час до начала брони."
                : "Заселить можно в день заезда.");
        }

        if (room is null)
        {
            return BookingErrors.RoomNotFound;
        }

        var status = await roomStatus.GetStatusAsync(room.Id, ct) ?? room.Status;
        return status switch
        {
            RoomStatus.Available => null,
            RoomStatus.Cleaning => Error.BusinessRule("room.not_ready", "Номер ещё на уборке. Дождитесь готовности или перенесите гостя в другой номер."),
            RoomStatus.Occupied => Error.Conflict("room.occupied", "В номере ещё другой гость — сначала выселите его."),
            _ => Error.BusinessRule("room.unavailable", "Номер на ремонте или вне продажи — перенесите гостя в другой номер."),
        };
    }

    private async Task<Result<BookingDto>> SaveAsync(Guid bookingId, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return BookingErrors.ConcurrencyConflict;
        }

        db.ChangeTracker.Clear();
        return await bookings.GetAsync(bookingId, ct);
    }

    private async Task<Booking?> LoadAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var query = db.Set<Booking>().Include(b => b.Rooms).Include(b => b.Services).Include(b => b.Extensions).Where(b => b.Id == id && b.HotelId == hotelId);
        return await (tracking ? query : query.AsNoTracking()).FirstOrDefaultAsync(ct);
    }

    private sealed record CheckOutPlan(bool Early, StayPeriod? Shortened, int UnusedUnits, decimal Credit, int LateMinutes, DateTimeOffset CleaningDueAt);
}
