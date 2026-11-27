using Hms.Modules.Audit.Contracts;
using Hms.Modules.Bookings.Application;
using Hms.Modules.Bookings.Contracts;
using Hms.Modules.Bookings.Domain;
using Hms.Modules.Finance.Contracts;
using Hms.Modules.Hotel.Contracts;
using Hms.Modules.Housekeeping.Contracts;
using Hms.Modules.Messaging.Contracts;
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
    IGuestNotifications notifications,
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

        var room = await rooms.GetAsync(booking.CurrentRoom.RoomId, ct);
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

        var room = await rooms.GetAsync(booking.CurrentRoom.RoomId, ct);
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
        if (booking.GuestId is { } checkedInGuest)
        {
            notifications.Enqueue(new GuestNotification(NotificationEvent.CheckInDone, checkedInGuest, booking.Id));
        }

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

        var roomId = booking.CurrentRoom.RoomId;
        var cleaning = await roomStatus.ChangeAsync(roomId, RoomStatus.Cleaning, RoomStatusSource.CheckOut, null, booking.Id, ct);
        if (cleaning.IsFailure)
        {
            db.ChangeTracker.Clear();
            return cleaning.Error;
        }

        await housekeeping.CreateAfterCheckOutAsync(roomId, booking.Id, plan.CleaningDueAt, ct);
        audit.Record(new AuditEntry("booking.checked_out", "Booking", booking.Id.ToString(),
            After: new { booking.Number, plan.Early, plan.Credit, Debt = Math.Max(0, debt) }));
        if (booking.GuestId is { } checkedOutGuest)
        {
            notifications.Enqueue(new GuestNotification(NotificationEvent.CheckOutDone, checkedOutGuest, booking.Id));
        }

        return await SaveAsync(booking.Id, ct);
    }

    private static readonly Error NotCheckedIn = Error.Conflict("booking.not_checked_in", "Гость не заселён — выселять некого.");

    private static readonly Error MoveNotAllowed = Error.Conflict("booking.move_not_checked_in", "Переселить можно только заселённого гостя. До заселения бронь переносят.");

    public async Task<Result<MoveOptionsDto>> MoveOptionsAsync(Guid bookingId, CancellationToken ct)
    {
        var booking = await LoadAsync(bookingId, tracking: false, ct);
        if (booking is null)
        {
            return BookingErrors.NotFound;
        }

        if (booking.Status != BookingStatus.CheckedIn || clock.UtcNow >= booking.EndsAt)
        {
            return MoveNotAllowed;
        }

        var current = await rooms.GetAsync(booking.CurrentRoom.RoomId, ct);
        var remaining = Remaining(booking);
        var currentPrice = current is null ? 0m : await RemainingPriceAsync(current, remaining, ct);
        var options = new List<MoveRoomOptionDto>();
        foreach (var room in await FreeRoomsAsync(booking, ct))
        {
            options.Add(new MoveRoomOptionDto(room.Id, room.Number, room.RoomTypeName, await RemainingPriceAsync(room, remaining, ct) - currentPrice));
        }

        return new MoveOptionsDto(current?.Number ?? "—", remaining.Units, booking.Kind == BookingKind.Hourly ? "Hour" : "Night", booking.TotalAmount,
            options.OrderBy(o => o.Number, StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// Переселение: новый номер «Занят», прежний — «Уборка» с задачей уборки; бронь, оплаты и история — те же.
    /// Цена: без изменения или с пересчётом оставшегося времени по цене нового номера (выбирает администратор).
    /// </summary>
    public async Task<Result<BookingDto>> MoveAsync(Guid bookingId, MoveGuestRequest r, CancellationToken ct)
    {
        var booking = await LoadAsync(bookingId, tracking: true, ct);
        if (booking is null)
        {
            return BookingErrors.NotFound;
        }

        if (booking.Status != BookingStatus.CheckedIn)
        {
            return MoveNotAllowed;
        }

        if (r.Version is { } version)
        {
            db.Entry(booking).Property(b => b.Version).OriginalValue = version;
        }

        var free = await FreeRoomsAsync(booking, ct);
        var target = free.FirstOrDefault(x => x.Id == r.RoomId);
        if (target is null)
        {
            return Error.Conflict("booking.move_room_unavailable", "Этот номер сейчас не готов или занят до выезда гостя. Выберите другой.");
        }

        var oldRoomId = booking.CurrentRoom.RoomId;
        var oldRoom = await rooms.GetAsync(oldRoomId, ct);
        var settings = await stays.SettingsAsync(ct);
        var now = clock.UtcNow;
        var remaining = Remaining(booking);
        var difference = r.Recalculate && oldRoom is not null
            ? await RemainingPriceAsync(target, remaining, ct) - await RemainingPriceAsync(oldRoom, remaining, ct)
            : 0m;
        var oldBlocked = oldRoom is null ? now : Stays.ForRoom(remaining.Stay with { StartsAt = now, EndsAt = now, BlockedUntil = now }, settings, oldRoom).BlockedUntil;
        var newBlocked = Stays.ForRoom(remaining.Stay with { StartsAt = now, EndsAt = booking.EndsAt, BlockedUntil = booking.EndsAt }, settings, target).BlockedUntil;

        var moved = booking.MoveGuest(target.Id, target.RoomTypeId, clock.Today, now, oldBlocked, newBlocked, difference, r.Reason, currentUser.UserId);
        if (moved.IsFailure)
        {
            return moved.Error;
        }

        var occupied = await roomStatus.ChangeAsync(target.Id, RoomStatus.Occupied, RoomStatusSource.CheckIn, "Переселение гостя", booking.Id, ct);
        var cleaning = occupied.IsFailure ? occupied : await roomStatus.ChangeAsync(oldRoomId, RoomStatus.Cleaning, RoomStatusSource.CheckOut, "Гость переселён", booking.Id, ct);
        if (cleaning.IsFailure)
        {
            db.ChangeTracker.Clear();
            return cleaning.Error;
        }

        await housekeeping.CreateAfterCheckOutAsync(oldRoomId, booking.Id, oldBlocked < now ? now : oldBlocked, ct);
        audit.Record(new AuditEntry("booking.room_moved", "Booking", booking.Id.ToString(),
            After: new { booking.Number, from = oldRoom?.Number, to = target.Number, r.Recalculate, Difference = difference }));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return BookingErrors.ConcurrencyConflict;
        }
        catch (DbUpdateException ex) when (ex.InnerException is System.Data.Common.DbException { SqlState: "23P01" })
        {
            // Параллельная бронь успела занять номер (ограничение EXCLUDE).
            db.ChangeTracker.Clear();
            return Error.Conflict("booking.move_room_unavailable", "Номер только что заняли. Выберите другой.");
        }

        db.ChangeTracker.Clear();
        return await bookings.GetAsync(booking.Id, ct);
    }

    /// <summary>
    /// Оставшееся время брони — с него считается разница в цене. Почасовая — с этого момента до выезда,
    /// округлённое вверх до получаса (минимум полчаса); посуточная — ночи, начиная с сегодняшней.
    /// </summary>
    private sealed record RemainingTime(StayPeriod Stay, decimal Units, bool FirstHourIsHalf);

    private RemainingTime Remaining(Booking b)
    {
        var now = clock.UtcNow;
        if (b.Kind == BookingKind.Hourly)
        {
            // Заселили раньше начала брони — оставшееся время считается с начала брони, а не с этого момента.
            var start = now > b.StartsAt ? now : b.StartsAt;
            var halves = RoomMoveTime.RemainingHalfHours(start, b.EndsAt);
            var hours = (halves + 1) / 2;

            // Цены часов берутся для целых часов, отсчитанных назад от выезда; при нечётном числе получасов
            // первый (неполный) час считается за половину.
            return new RemainingTime(new StayPeriod(b.Kind, b.CheckIn, b.CheckOut, b.EndsAt.AddHours(-hours), b.EndsAt, b.EndsAt, hours), halves / 2m, halves % 2 == 1);
        }

        var from = clock.Today > b.CheckIn ? clock.Today : b.CheckIn;
        var nights = Math.Max(0, b.CheckOut.DayNumber - from.DayNumber);
        return new RemainingTime(new StayPeriod(b.Kind, from, b.CheckOut, now, b.EndsAt, b.EndsAt, nights), nights, false);
    }

    /// <summary>Цена оставшегося времени в номере по текущим ценам (без скидок и услуг).</summary>
    private async Task<decimal> RemainingPriceAsync(RoomInfo room, RemainingTime remaining, CancellationToken ct)
    {
        if (remaining.Stay.Units <= 0)
        {
            return 0m;
        }

        var quote = await stays.PriceAsync(room, remaining.Stay, null, [], ct);
        if (quote.IsFailure)
        {
            return 0m;
        }

        var total = quote.Value.AccommodationTotal;
        if (remaining.FirstHourIsHalf && quote.Value.Hours is { Count: > 0 } hours)
        {
            total -= Math.Round(hours.OrderBy(h => h.From).First().UnitPrice / 2, 2, MidpointRounding.AwayFromZero);
        }

        return total;
    }

    /// <summary>Готовые («Свободен») номера в продаже, вмещающие гостей, свободные с этого момента до выезда с учётом уборки.</summary>
    private async Task<List<RoomInfo>> FreeRoomsAsync(Booking booking, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var hourly = booking.Kind == BookingKind.Hourly;
        var settings = await stays.SettingsAsync(ct);
        var currentId = booking.CurrentRoom.RoomId;
        var candidates = (await rooms.ListSellableAsync(null, ct))
            .Where(x => x.Id != currentId && x.Status == RoomStatus.Available && x.Capacity >= booking.Adults + booking.Children && (!hourly || x.SellsHourly))
            .ToList();
        var stay = new StayPeriod(booking.Kind, clock.Today, booking.CheckOut, now, booking.EndsAt, booking.EndsAt, booking.Units);
        var busy = new HashSet<Guid>();
        foreach (var group in candidates.GroupBy(x => Stays.ForRoom(stay, settings, x).BlockedUntil))
        {
            busy.UnionWith(await stays.BusyRoomsAsync(group.Select(x => x.Id).ToList(), stay with { BlockedUntil = group.Key }, booking.Id, ct));
        }

        return candidates.Where(x => !busy.Contains(x.Id)).ToList();
    }

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
        var room = await rooms.GetAsync(booking.CurrentRoom.RoomId, ct);
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
