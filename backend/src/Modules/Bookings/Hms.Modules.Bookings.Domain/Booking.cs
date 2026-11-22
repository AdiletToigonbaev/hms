using Hms.Modules.Bookings.Contracts;
using Hms.SharedKernel;

namespace Hms.Modules.Bookings.Domain;

public enum BookingSource
{
    Admin,
    Web,
}

/// <summary>Вид брони (решение заказчика 2026-11-20): по суткам (ночи) или по часам.</summary>
public enum BookingKind
{
    Nightly,
    Hourly,
}

/// <summary>
/// Переходы статусов брони (docs/database/database-design.md §4). Остальные запрещены.
/// </summary>
public static class BookingStatusMachine
{
    private static readonly Dictionary<BookingStatus, BookingStatus[]> Allowed = new()
    {
        [BookingStatus.Pending] = [BookingStatus.AwaitingPayment, BookingStatus.Confirmed, BookingStatus.Cancelled, BookingStatus.Expired],
        [BookingStatus.AwaitingPayment] = [BookingStatus.Confirmed, BookingStatus.Expired, BookingStatus.Cancelled],
        [BookingStatus.Confirmed] = [BookingStatus.CheckedIn, BookingStatus.Cancelled, BookingStatus.NoShow],
        [BookingStatus.CheckedIn] = [BookingStatus.CheckedOut],
    };

    public static bool CanTransition(BookingStatus from, BookingStatus to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    /// <summary>Занимает ли бронь номер (участвует в ограничении против пересечений).</summary>
    public static bool IsBlocking(BookingStatus status) =>
        status is BookingStatus.Pending or BookingStatus.AwaitingPayment or BookingStatus.Confirmed or BookingStatus.CheckedIn;

    /// <summary>Продлить можно подтверждённую бронь и бронь заселённого гостя.</summary>
    public static bool CanExtend(BookingStatus status) => status is BookingStatus.Confirmed or BookingStatus.CheckedIn;

    /// <summary>Перенести (время, даты, номер) можно до заселения.</summary>
    public static bool CanReschedule(BookingStatus status) =>
        status is BookingStatus.Pending or BookingStatus.AwaitingPayment or BookingStatus.Confirmed;

    /// <summary>Неподтверждённая бронь держит номер ограниченное время (<see cref="Booking.HoldUntil"/>).</summary>
    public static bool IsHeld(BookingStatus status) => status is BookingStatus.Pending or BookingStatus.AwaitingPayment;
}

/// <summary>За что начислен штраф.</summary>
public enum PenaltyKind
{
    LateCancel,
    NoShow,
}

/// <summary>Правило штрафа одного вида брони (из настроек гостиницы, roadmap §6.1 и §6.1a).</summary>
/// <param name="FreeCancelBefore">За сколько до начала отмена бесплатна.</param>
/// <param name="LateCancelUnits">Штраф за позднюю отмену — часов (почасовая) или ночей (посуточная).</param>
/// <param name="NoShowUnits">Штраф за неявку — часов или ночей.</param>
public sealed record PenaltyRules(TimeSpan FreeCancelBefore, int LateCancelUnits, int NoShowUnits);

/// <summary>Условия отмены на текущий момент.</summary>
/// <param name="FreeUntil">До какого момента отмена бесплатна (для подтверждённой брони).</param>
/// <param name="Units">За сколько часов/ночей штраф.</param>
public sealed record CancellationTerms(bool IsFree, DateTimeOffset? FreeUntil, decimal Penalty, int Units);

/// <summary>
/// Расчёт штрафов. Штрафуется только подтверждённая бронь: неподтверждённая номер не гарантировала.
/// Цена единицы — средняя цена часа (ночи) брони после скидки, с продлениями; штраф не больше стоимости проживания.
/// </summary>
public static class BookingPenalties
{
    public static CancellationTerms ForCancel(Booking booking, PenaltyRules rules, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(booking);
        ArgumentNullException.ThrowIfNull(rules);
        // Неподтверждённая бронь номер не гарантировала; штраф 0 — штрафов нет (по умолчанию у гостиницы штрафы выключены).
        if (booking.Status != BookingStatus.Confirmed || rules.LateCancelUnits <= 0)
        {
            return new CancellationTerms(true, null, 0m, 0);
        }

        var freeUntil = booking.StartsAt - rules.FreeCancelBefore;
        return now <= freeUntil
            ? new CancellationTerms(true, freeUntil, 0m, 0)
            : new CancellationTerms(false, freeUntil, Amount(booking, rules.LateCancelUnits), Math.Min(rules.LateCancelUnits, booking.Units));
    }

    public static decimal ForNoShow(Booking booking, PenaltyRules rules)
    {
        ArgumentNullException.ThrowIfNull(booking);
        ArgumentNullException.ThrowIfNull(rules);
        return Amount(booking, rules.NoShowUnits);
    }

    /// <summary>Штраф за <paramref name="units"/> часов/ночей по средней цене единицы брони.</summary>
    public static decimal Amount(Booking booking, int units)
    {
        ArgumentNullException.ThrowIfNull(booking);
        var accommodation = booking.AccommodationAmount - booking.DiscountAmount + booking.ExtensionsAmount;
        if (units <= 0 || booking.Units <= 0 || accommodation <= 0)
        {
            return 0m;
        }

        var amount = Math.Round(accommodation / booking.Units * Math.Min(units, booking.Units), 2, MidpointRounding.AwayFromZero);
        return Math.Min(amount, accommodation);
    }
}

/// <summary>
/// Период брони во времени. Номер занят на [StartsAt, BlockedUntil): до выезда и ещё на время уборки.
/// Для посуточной брони StartsAt/EndsAt — часы заезда и выезда из настроек; Units — ночи; для почасовой — часы.
/// </summary>
public sealed record StayPeriod(
    BookingKind Kind,
    DateOnly CheckIn,
    DateOnly CheckOut,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    DateTimeOffset BlockedUntil,
    int Units)
{
    /// <summary>Пересекается ли с занятостью другой брони.</summary>
    public bool Overlaps(DateTimeOffset otherStart, DateTimeOffset otherBlockedUntil) => StartsAt < otherBlockedUntil && otherStart < BlockedUntil;
}

/// <summary>
/// Номер брони для людей (решение заказчика 2026-11-21): вид + день создания + номер за день — «Ч-2111-07»
/// (Ч — почасовая, С — посуточная; создана 21.11; седьмая за день). Номер не меняется: вид брони и дата создания
/// при переносе не меняются. Год в номере не пишется — хранится отдельно (<see cref="Booking.NumberYear"/>),
/// уникальность — по году и номеру. Номер за день — от 01, с сотой брони — трёхзначный.
/// </summary>
public static class BookingNumbers
{
    public const string HourlyPrefix = "Ч";
    public const string NightlyPrefix = "С";

    public static string Format(BookingKind kind, DateOnly createdOn, int sequence)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sequence, 1);
        var prefix = kind == BookingKind.Hourly ? HourlyPrefix : NightlyPrefix;
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{prefix}-{createdOn:ddMM}-{sequence:00}");
    }
}

/// <summary>Счётчик номеров броней за день гостиницы (выдаётся атомарно в БД).</summary>
public sealed class BookingNumberCounter
{
    private BookingNumberCounter()
    {
    }

    public Guid HotelId { get; private set; }

    public DateOnly Day { get; private set; }

    public int Last { get; private set; }
}

/// <summary>Контакт гостя в брони. Карточка гостя в CRM и связь с ней — Sprint 4 (модуль Guests).</summary>
public sealed record GuestContact(string FullName, string? Phone, string? Email);

/// <summary>
/// Бронь (ТЗ §5). Стоимость фиксируется снимком при создании (BOOK-4); продление добавляет свою строку
/// по ценам на момент продления. Все изменения пишутся в историю (BOOK-7).
/// Номер на время [StartsAt, BlockedUntil) занимает строка <see cref="BookingRoom"/> — по ней работает ограничение БД.
/// </summary>
public sealed class Booking
{
    private readonly List<BookingRoom> _rooms = [];
    private readonly List<BookingServiceLine> _services = [];
    private readonly List<BookingHistoryEntry> _history = [];
    private readonly List<BookingExtension> _extensions = [];

    private Booking()
    {
        Number = string.Empty;
        GuestName = string.Empty;
        PriceSnapshot = "{}";
    }

    public Guid Id { get; private set; }

    public Guid HotelId { get; private set; }

    /// <summary>Номер для людей: «Ч-2111-07» (<see cref="BookingNumbers"/>). У броней до 2026-11-21 — «26-000042».</summary>
    public string Number { get; private set; }

    /// <summary>Год создания по времени гостиницы: номер уникален в пределах года.</summary>
    public int NumberYear { get; private set; }

    public BookingStatus Status { get; private set; }

    public BookingSource Source { get; private set; }

    public BookingKind Kind { get; private set; }

    /// <summary>Дата заезда (для почасовой — дата начала) по времени гостиницы.</summary>
    public DateOnly CheckIn { get; private set; }

    /// <summary>Дата выезда (для почасовой — дата окончания).</summary>
    public DateOnly CheckOut { get; private set; }

    /// <summary>Начало проживания, UTC.</summary>
    public DateTimeOffset StartsAt { get; private set; }

    /// <summary>Окончание проживания (выезд), UTC.</summary>
    public DateTimeOffset EndsAt { get; private set; }

    /// <summary>Для почасовой брони — число часов (с продлениями).</summary>
    public int? Hours { get; private set; }

    public int Adults { get; private set; }

    public int Children { get; private set; }

    public string GuestName { get; private set; }

    public string? GuestPhone { get; private set; }

    public string? GuestEmail { get; private set; }

    /// <summary>Карточка гостя в модуле Guests. Поля Guest* — снимок контакта на момент брони.</summary>
    public Guid? GuestId { get; private set; }

    public string? Comment { get; private set; }

    public Guid? DiscountId { get; private set; }

    public string? PromoCode { get; private set; }

    public decimal AccommodationAmount { get; private set; }

    public decimal DiscountAmount { get; private set; }

    public decimal ServicesAmount { get; private set; }

    /// <summary>Сумма продлений (по ценам на момент продления).</summary>
    public decimal ExtensionsAmount { get; private set; }

    public decimal TotalAmount { get; private set; }

    /// <summary>Снимок расчёта при создании (цены по ночам или часам, тарифы, скидка, услуги) — JSON.</summary>
    public string PriceSnapshot { get; private set; }

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string? CancelReason { get; private set; }

    /// <summary>До какого момента неподтверждённая бронь держит номер; потом фоновая задача переводит её в Expired.</summary>
    public DateTimeOffset? HoldUntil { get; private set; }

    /// <summary>Фактическое время заселения.</summary>
    public DateTimeOffset? ActualCheckInAt { get; private set; }

    /// <summary>Фактическое время выселения.</summary>
    public DateTimeOffset? ActualCheckOutAt { get; private set; }

    /// <summary>Перерасчёт при досрочном выезде (вычтен из стоимости): неиспользованные ночи/часы по правилам гостиницы.</summary>
    public decimal EarlyDepartureCredit { get; private set; }

    /// <summary>Изменение стоимости при переселении (пересчёт оставшегося времени по цене нового номера); может быть отрицательным.</summary>
    public decimal RoomChangeAmount { get; private set; }

    /// <summary>Когда бронь последний раз переносили: продления до этого момента уже вошли в новую цену.</summary>
    public DateTimeOffset? RescheduledAt { get; private set; }

    /// <summary>Продления, которые входят в текущую стоимость (после последнего переноса).</summary>
    public IEnumerable<BookingExtension> ActiveExtensions => _extensions.Where(e => RescheduledAt is null || e.CreatedAt > RescheduledAt);

    /// <summary>
    /// Начисленный штраф (поздняя отмена, неявка). Только начислен: принимается и учитывается модулем оплат (Sprint 6).
    /// </summary>
    public decimal PenaltyAmount { get; private set; }

    public PenaltyKind? PenaltyKind { get; private set; }

    /// <summary>Штраф полагался по правилам, но сотрудник с правом снял его (исключение из правил).</summary>
    public bool PenaltyWaived { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public IReadOnlyList<BookingRoom> Rooms => _rooms;

    /// <summary>
    /// Номер, в котором гость сейчас (или будет): последний отрезок. После переселения у брони несколько отрезков —
    /// прежние номера до момента переселения и текущий до выезда.
    /// </summary>
    public BookingRoom CurrentRoom => _rooms.OrderByDescending(x => x.StartsAt).First();

    public IReadOnlyList<BookingServiceLine> Services => _services;

    public IReadOnlyList<BookingHistoryEntry> History => _history;

    public IReadOnlyList<BookingExtension> Extensions => _extensions;

    public int Nights => Kind == BookingKind.Nightly ? CheckOut.DayNumber - CheckIn.DayNumber : 0;

    /// <summary>Часы (почасовая) или ночи (посуточная) — единицы цены и штрафов.</summary>
    public int Units => Kind == BookingKind.Hourly ? Hours ?? 0 : Nights;

    public static Booking Create(
        Guid hotelId,
        string number,
        BookingSource source,
        StayPeriod stay,
        int adults,
        int children,
        GuestContact guest,
        Guid? guestId,
        string? comment,
        Guid roomId,
        Guid roomTypeId,
        BookingPrice price,
        bool confirm,
        Guid? createdBy,
        DateTimeOffset now,
        DateTimeOffset? holdUntil = null,
        int? numberYear = null)
    {
        ArgumentNullException.ThrowIfNull(stay);
        ArgumentNullException.ThrowIfNull(guest);
        ArgumentNullException.ThrowIfNull(price);
        var booking = new Booking
        {
            Id = Guid.CreateVersion7(),
            HotelId = hotelId,
            Number = number,
            NumberYear = numberYear ?? now.Year,
            Source = source,
            Kind = stay.Kind,
            Status = confirm ? BookingStatus.Confirmed : BookingStatus.Pending,
            CheckIn = stay.CheckIn,
            CheckOut = stay.CheckOut,
            StartsAt = stay.StartsAt,
            EndsAt = stay.EndsAt,
            Hours = stay.Kind == BookingKind.Hourly ? stay.Units : null,
            Adults = adults,
            Children = children,
            GuestName = guest.FullName.Trim(),
            GuestPhone = guest.Phone,
            GuestEmail = guest.Email,
            GuestId = guestId,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
            DiscountId = price.DiscountId,
            PromoCode = price.PromoCode,
            AccommodationAmount = price.Accommodation,
            DiscountAmount = price.Discount,
            ServicesAmount = price.Services,
            TotalAmount = price.Total,
            PriceSnapshot = price.SnapshotJson,
            ConfirmedAt = confirm ? now : null,
            HoldUntil = confirm ? null : holdUntil,
            CreatedBy = createdBy,
            CreatedAt = now,
            UpdatedAt = now,
        };
        booking._rooms.Add(new BookingRoom(booking.Id, roomId, roomTypeId, stay, BookingStatusMachine.IsBlocking(booking.Status)));
        booking._services.AddRange(price.ServiceLines.Select(s => new BookingServiceLine(booking.Id, s.ServiceId, s.Name, s.Quantity, s.UnitPrice, s.PerNight, s.Total, createdBy, now)));
        booking.AddHistory(
            "created",
            null,
            new { Kind = stay.Kind.ToString(), Status = booking.Status.ToString(), stay.StartsAt, stay.EndsAt, roomId, price.Total },
            createdBy,
            now,
            null);
        return booking;
    }

    public Result Confirm(Guid? by, DateTimeOffset now)
    {
        var result = ChangeStatus(BookingStatus.Confirmed, by, now, null);
        if (result.IsSuccess)
        {
            ConfirmedAt = now;
            HoldUntil = null;
        }

        return result;
    }

    public Result Cancel(string reason, Guid? by, DateTimeOffset now) => Cancel(reason, 0m, false, by, now);

    /// <summary>Отмена. <paramref name="penalty"/> — штраф по правилам; <paramref name="waived"/> — снят сотрудником с правом.</summary>
    public Result Cancel(string reason, decimal penalty, bool waived, Guid? by, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("booking.reason_required", "Укажите причину отмены.");
        }

        var result = ChangeStatus(BookingStatus.Cancelled, by, now, reason.Trim());
        if (result.IsSuccess)
        {
            CancelledAt = now;
            CancelReason = reason.Trim();
            HoldUntil = null;
            ApplyPenalty(Domain.PenaltyKind.LateCancel, penalty, waived, by, now);
        }

        return result;
    }

    /// <summary>
    /// Неявка: гость не пришёл. Номер освобождается (статус NoShow не блокирует), начисляется штраф по правилам.
    /// <paramref name="by"/> = null — отметила фоновая задача.
    /// </summary>
    public Result MarkNoShow(decimal penalty, bool waived, Guid? by, DateTimeOffset now)
    {
        if (Status == BookingStatus.Confirmed && now < StartsAt)
        {
            return Error.BusinessRule("booking.no_show_too_early", "Неявку можно отметить только после времени начала брони.");
        }

        var result = ChangeStatus(BookingStatus.NoShow, by, now, null);
        if (result.IsSuccess)
        {
            ApplyPenalty(Domain.PenaltyKind.NoShow, penalty, waived, by, now);
        }

        return result;
    }

    /// <summary>Заселение: гость приехал. Можно немного раньше начала (окно проверяет сервис), не позже выезда.</summary>
    public Result CheckInGuest(Guid? by, DateTimeOffset now)
    {
        if (Status != BookingStatus.Confirmed)
        {
            return Status is BookingStatus.Pending or BookingStatus.AwaitingPayment
                ? Error.BusinessRule("booking.checkin_not_confirmed", "Сначала подтвердите бронь.")
                : Error.Conflict("booking.invalid_transition", $"Нельзя заселить: бронь в статусе «{Status}».");
        }

        if (now >= EndsAt)
        {
            return Error.BusinessRule("booking.checkin_too_late", "Время брони уже закончилось — заселить нельзя. Создайте новую бронь.");
        }

        var result = ChangeStatus(BookingStatus.CheckedIn, by, now, null);
        if (result.IsSuccess)
        {
            ActualCheckInAt = now;
            HoldUntil = null;
        }

        return result;
    }

    /// <summary>
    /// Выселение. При досрочном выезде (<paramref name="shortened"/>) время брони сокращается до фактического,
    /// а неиспользованное время вычитается из стоимости (<paramref name="credit"/>) — по правилам гостиницы.
    /// </summary>
    public Result CheckOutGuest(Guid? by, DateTimeOffset now, StayPeriod? shortened, decimal credit)
    {
        if (Status != BookingStatus.CheckedIn)
        {
            return Error.Conflict("booking.invalid_transition", $"Нельзя выселить: бронь в статусе «{Status}».");
        }

        var plannedEnd = EndsAt;
        var result = ChangeStatus(BookingStatus.CheckedOut, by, now, null);
        if (result.IsFailure)
        {
            return result;
        }

        ActualCheckOutAt = now;
        if (shortened is not null && shortened.EndsAt < EndsAt)
        {
            EndsAt = shortened.EndsAt;
            CheckOut = shortened.CheckOut;
            CurrentRoom.Extend(shortened);
        }

        if (credit > 0)
        {
            var applied = Math.Min(credit, AccommodationAmount - DiscountAmount + ExtensionsAmount);
            EarlyDepartureCredit = applied;
            TotalAmount -= applied;
            AddHistory("early_departure", new { EndsAt = plannedEnd }, new { EndsAt, Credit = applied }, by, now, null);
        }

        return Result.Success();
    }

    /// <summary>
    /// Переселение заселённого гостя в другой номер. Прежний отрезок заканчивается сейчас (номер занят ещё на время уборки),
    /// новый — с этого момента до выезда. Цена: без изменения (<paramref name="priceChange"/> = 0) или с пересчётом оставшегося времени.
    /// </summary>
    public Result MoveGuest(Guid roomId, Guid roomTypeId, DateOnly today, DateTimeOffset now, DateTimeOffset previousBlockedUntil,
        DateTimeOffset newBlockedUntil, decimal priceChange, string? reason, Guid? by)
    {
        if (Status != BookingStatus.CheckedIn)
        {
            return Error.Conflict("booking.move_not_checked_in", "Переселить можно только заселённого гостя. До заселения бронь переносят.");
        }

        if (now >= EndsAt)
        {
            return Error.BusinessRule("booking.move_too_late", "Время брони уже закончилось — продлите бронь или выселите гостя.");
        }

        var current = CurrentRoom;
        if (current.RoomId == roomId)
        {
            return Error.Validation("booking.move_same_room", "Гость уже в этом номере.");
        }

        var units = Kind == BookingKind.Hourly ? Hours ?? 0 : Nights;
        var from = current.RoomId;
        if (now <= current.StartsAt)
        {
            // Заселили раньше начала брони и переселяют до её начала — просто другой номер, без разделения.
            current.Move(roomId, roomTypeId, new StayPeriod(Kind, current.CheckIn, current.CheckOut, current.StartsAt, current.EndsAt, newBlockedUntil, units));
        }
        else
        {
            current.Close(now, today, previousBlockedUntil);
            _rooms.Add(new BookingRoom(Id, roomId, roomTypeId,
                new StayPeriod(Kind, today < CheckOut ? today : CheckOut, CheckOut, now, EndsAt, newBlockedUntil < EndsAt ? EndsAt : newBlockedUntil, units), isBlocking: true));
        }

        // Стоимость не уходит в минус, даже если новый номер намного дешевле.
        var change = Math.Max(priceChange, -TotalAmount);
        if (change != 0)
        {
            RoomChangeAmount += change;
            TotalAmount += change;
        }

        UpdatedAt = now;
        AddHistory("room_moved", new { RoomId = from }, new { RoomId = roomId, PriceChange = change }, by, now, string.IsNullOrWhiteSpace(reason) ? null : reason.Trim());
        return Result.Success();
    }

    /// <summary>Неподтверждённая бронь не подтверждена вовремя — номер освобождается.</summary>
    public Result Expire(DateTimeOffset now)
    {
        if (!BookingStatusMachine.IsHeld(Status) || HoldUntil is not { } until || until > now)
        {
            return Error.Conflict("booking.not_expired", "Срок удержания брони ещё не истёк.");
        }

        var result = ChangeStatus(BookingStatus.Expired, null, now, null);
        if (result.IsSuccess)
        {
            HoldUntil = null;
        }

        return result;
    }

    /// <summary>
    /// Перенос до заселения: новое время (и длительность) и/или другой номер. Цена — по текущим ценам на новое время.
    /// Если бронь продлевали, её время (с продлениями) пересчитывается целиком одной ценой: прежние продления остаются в истории,
    /// но в стоимость больше не входят (<see cref="RescheduledAt"/>).
    /// </summary>
    public Result Reschedule(StayPeriod stay, Guid roomId, Guid roomTypeId, BookingPrice price, Guid? by, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(stay);
        ArgumentNullException.ThrowIfNull(price);
        if (!BookingStatusMachine.CanReschedule(Status))
        {
            return Error.Conflict("booking.reschedule_not_allowed", "Перенести можно только бронь до заселения.");
        }

        if (stay.Kind != Kind)
        {
            return Error.Validation("booking.reschedule_kind", "Нельзя сменить вид брони (часы/сутки) — отмените и создайте новую.");
        }

        var line = CurrentRoom;
        var before = new { StartsAt, EndsAt, RoomId = line.RoomId, TotalAmount };
        CheckIn = stay.CheckIn;
        CheckOut = stay.CheckOut;
        StartsAt = stay.StartsAt;
        EndsAt = stay.EndsAt;
        Hours = Kind == BookingKind.Hourly ? stay.Units : null;
        line.Move(roomId, roomTypeId, stay);

        AccommodationAmount = price.Accommodation;
        DiscountAmount = price.Discount;
        ServicesAmount = price.Services;
        ExtensionsAmount = 0m;
        RescheduledAt = now;
        TotalAmount = price.Total;
        DiscountId = price.DiscountId;
        PromoCode = price.PromoCode;
        PriceSnapshot = price.SnapshotJson;
        foreach (var service in _services)
        {
            var updated = price.ServiceLines.FirstOrDefault(s => s.ServiceId == service.ServiceId);
            if (updated is not null)
            {
                service.Reprice(updated.Quantity, updated.UnitPrice, updated.Total);
            }
        }

        if (HoldUntil is { } hold && stay.StartsAt > now && stay.StartsAt < hold)
        {
            HoldUntil = stay.StartsAt;
        }

        UpdatedAt = now;
        AddHistory("rescheduled", before, new { StartsAt, EndsAt, RoomId = roomId, TotalAmount }, by, now, null);
        return Result.Success();
    }

    private void ApplyPenalty(PenaltyKind kind, decimal penalty, bool waived, Guid? by, DateTimeOffset now)
    {
        if (penalty <= 0)
        {
            return;
        }

        PenaltyKind = kind;
        PenaltyWaived = waived;
        PenaltyAmount = waived ? 0m : penalty;
        AddHistory(waived ? "penalty_waived" : "penalty_charged", null, new { Kind = kind.ToString(), Amount = penalty }, by, now, null);
    }

    /// <summary>Смена статуса по машине переходов; пересчитывает блокировку номера в той же единице работы.</summary>
    public Result ChangeStatus(BookingStatus target, Guid? by, DateTimeOffset now, string? comment)
    {
        if (!BookingStatusMachine.CanTransition(Status, target))
        {
            return Error.Conflict("booking.invalid_transition", $"Нельзя перевести бронь из статуса «{Status}» в «{target}».");
        }

        var from = Status;
        Status = target;
        foreach (var room in _rooms)
        {
            room.SetBlocking(BookingStatusMachine.IsBlocking(target));
        }

        UpdatedAt = now;
        AddHistory("status_changed", new { Status = from.ToString() }, new { Status = target.ToString() }, by, now, comment);
        return Result.Success();
    }

    /// <summary>
    /// Продление (решение заказчика 2026-11-20): новый конец проживания, добавленное время — по ценам на момент продления.
    /// Свободен ли номер, проверяет приложение и ограничение БД.
    /// </summary>
    public Result Extend(StayPeriod extended, int addedUnits, decimal amount, string snapshotJson, Guid? by, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(extended);
        if (!BookingStatusMachine.CanExtend(Status))
        {
            return Error.Conflict("booking.extend_not_allowed", "Продлить можно только подтверждённую бронь или бронь заселённого гостя.");
        }

        if (extended.Kind != Kind || extended.EndsAt <= EndsAt)
        {
            return Error.Validation("booking.extend_invalid", "Новое время выезда должно быть позже текущего.");
        }

        var previousEnd = EndsAt;
        _extensions.Add(new BookingExtension(Id, Kind, addedUnits, previousEnd, extended.EndsAt, amount, snapshotJson, by, now));
        EndsAt = extended.EndsAt;
        CheckOut = extended.CheckOut;
        if (Kind == BookingKind.Hourly)
        {
            Hours = (Hours ?? 0) + addedUnits;
        }

        CurrentRoom.Extend(extended);

        ExtensionsAmount += amount;
        TotalAmount += amount;
        UpdatedAt = now;
        AddHistory(
            "extended",
            new { EndsAt = previousEnd },
            new { extended.EndsAt, Added = addedUnits, Unit = Kind == BookingKind.Hourly ? "Hour" : "Night", Amount = amount },
            by,
            now,
            null);
        return Result.Success();
    }

    public void UpdateDetails(GuestContact guest, int adults, int children, string? comment, Guid? by, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(guest);
        var before = new { GuestName, GuestPhone, GuestEmail, Adults, Children, Comment };
        GuestName = guest.FullName.Trim();
        GuestPhone = guest.Phone;
        GuestEmail = guest.Email;
        Adults = adults;
        Children = children;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        UpdatedAt = now;
        AddHistory("details_changed", before, new { GuestName, GuestPhone, GuestEmail, Adults, Children, Comment }, by, now, null);
    }

    private void AddHistory(string eventType, object? oldValues, object? newValues, Guid? by, DateTimeOffset now, string? comment) =>
        _history.Add(new BookingHistoryEntry(Id, eventType, oldValues, newValues, by, now, comment));
}

/// <summary>
/// Номер в брони. Занимает номер на [StartsAt, BlockedUntil), если <see cref="IsBlocking"/>
/// (Pending, AwaitingPayment, Confirmed, CheckedIn). CheckIn/CheckOut — даты по времени гостиницы (для отчётов и «Бронь сегодня»).
/// </summary>
public sealed class BookingRoom
{
    private BookingRoom()
    {
    }

    public BookingRoom(Guid bookingId, Guid roomId, Guid roomTypeId, StayPeriod stay, bool isBlocking)
    {
        ArgumentNullException.ThrowIfNull(stay);
        Id = Guid.CreateVersion7();
        BookingId = bookingId;
        RoomId = roomId;
        RoomTypeId = roomTypeId;
        CheckIn = stay.CheckIn;
        CheckOut = stay.CheckOut;
        StartsAt = stay.StartsAt;
        EndsAt = stay.EndsAt;
        BlockedUntil = stay.BlockedUntil;
        IsBlocking = isBlocking;
    }

    public Guid Id { get; private set; }

    public Guid BookingId { get; private set; }

    public Guid RoomId { get; private set; }

    public Guid RoomTypeId { get; private set; }

    public DateOnly CheckIn { get; private set; }

    public DateOnly CheckOut { get; private set; }

    public DateTimeOffset StartsAt { get; private set; }

    public DateTimeOffset EndsAt { get; private set; }

    /// <summary>Конец занятости: выезд + время на уборку.</summary>
    public DateTimeOffset BlockedUntil { get; private set; }

    public bool IsBlocking { get; private set; }

    internal void SetBlocking(bool blocking) => IsBlocking = blocking;

    internal void Move(Guid roomId, Guid roomTypeId, StayPeriod stay)
    {
        RoomId = roomId;
        RoomTypeId = roomTypeId;
        CheckIn = stay.CheckIn;
        CheckOut = stay.CheckOut;
        StartsAt = stay.StartsAt;
        EndsAt = stay.EndsAt;
        BlockedUntil = stay.BlockedUntil;
    }

    internal void Extend(StayPeriod extended)
    {
        CheckOut = extended.CheckOut;
        EndsAt = extended.EndsAt;
        BlockedUntil = extended.BlockedUntil;
    }

    /// <summary>Гость переселён: отрезок заканчивается сейчас, номер занят ещё на время уборки.</summary>
    internal void Close(DateTimeOffset now, DateOnly today, DateTimeOffset blockedUntil)
    {
        CheckOut = today < CheckIn ? CheckIn : today;
        EndsAt = now;
        BlockedUntil = blockedUntil < now ? now : blockedUntil;
    }
}

/// <summary>Продление брони: на сколько, до какого времени и за какую сумму (снимок цен на момент продления).</summary>
public sealed class BookingExtension
{
    private BookingExtension()
    {
        PriceSnapshot = "{}";
    }

    public BookingExtension(Guid bookingId, BookingKind unit, int quantity, DateTimeOffset previousEndsAt, DateTimeOffset newEndsAt, decimal amount, string priceSnapshot, Guid? createdBy, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        BookingId = bookingId;
        Unit = unit;
        Quantity = quantity;
        PreviousEndsAt = previousEndsAt;
        NewEndsAt = newEndsAt;
        Amount = amount;
        PriceSnapshot = priceSnapshot;
        CreatedBy = createdBy;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid BookingId { get; private set; }

    /// <summary>Nightly — ночи, Hourly — часы.</summary>
    public BookingKind Unit { get; private set; }

    public int Quantity { get; private set; }

    public DateTimeOffset PreviousEndsAt { get; private set; }

    public DateTimeOffset NewEndsAt { get; private set; }

    public decimal Amount { get; private set; }

    public string PriceSnapshot { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}

public sealed class BookingServiceLine
{
    private BookingServiceLine()
    {
        Name = string.Empty;
    }

    public BookingServiceLine(Guid bookingId, Guid serviceId, string name, int quantity, decimal unitPrice, bool perNight, decimal total, Guid? addedBy, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        BookingId = bookingId;
        ServiceId = serviceId;
        Name = name;
        Quantity = quantity;
        UnitPrice = unitPrice;
        PerNight = perNight;
        Total = total;
        AddedBy = addedBy;
        AddedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid BookingId { get; private set; }

    public Guid ServiceId { get; private set; }

    /// <summary>Название на момент заказа (снимок).</summary>
    public string Name { get; private set; }

    public int Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    public bool PerNight { get; private set; }

    public decimal Total { get; private set; }

    public Guid? AddedBy { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }

    /// <summary>Пересчёт при переносе брони (число ночей и цены могли измениться).</summary>
    internal void Reprice(int quantity, decimal unitPrice, decimal total)
    {
        Quantity = quantity;
        UnitPrice = unitPrice;
        Total = total;
    }
}

/// <summary>Запись истории брони (BOOK-7). Только добавление.</summary>
public sealed class BookingHistoryEntry
{
    private BookingHistoryEntry()
    {
        EventType = string.Empty;
    }

    public BookingHistoryEntry(Guid bookingId, string eventType, object? oldValues, object? newValues, Guid? changedBy, DateTimeOffset at, string? comment)
    {
        Id = Guid.CreateVersion7();
        BookingId = bookingId;
        EventType = eventType;
        OldValues = oldValues is null ? null : System.Text.Json.JsonSerializer.Serialize(oldValues, System.Text.Json.JsonSerializerOptions.Web);
        NewValues = newValues is null ? null : System.Text.Json.JsonSerializer.Serialize(newValues, System.Text.Json.JsonSerializerOptions.Web);
        ChangedBy = changedBy;
        ChangedAt = at;
        Comment = comment;
    }

    public Guid Id { get; private set; }

    public Guid BookingId { get; private set; }

    public string EventType { get; private set; }

    public string? OldValues { get; private set; }

    public string? NewValues { get; private set; }

    public Guid? ChangedBy { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    public string? Comment { get; private set; }
}

/// <summary>Зафиксированная стоимость брони (из расчёта модуля Pricing).</summary>
public sealed record BookingPrice(
    decimal Accommodation,
    decimal Discount,
    decimal Services,
    decimal Total,
    Guid? DiscountId,
    string? PromoCode,
    IReadOnlyList<BookingPriceServiceLine> ServiceLines,
    string SnapshotJson);

public sealed record BookingPriceServiceLine(Guid ServiceId, string Name, int Quantity, decimal UnitPrice, bool PerNight, decimal Total);

/// <summary>Переселение с пересчётом: сколько оставшегося времени почасовой брони идёт в расчёт разницы цены.</summary>
public static class RoomMoveTime
{
    /// <summary>Число получасов от <paramref name="from"/> до выезда, округлённое вверх; минимум один получас.</summary>
    public static int RemainingHalfHours(DateTimeOffset from, DateTimeOffset endsAt) =>
        Math.Max(1, (int)Math.Ceiling((endsAt - from).TotalMinutes / 30));
}
