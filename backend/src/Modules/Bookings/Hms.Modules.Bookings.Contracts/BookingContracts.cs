namespace Hms.Modules.Bookings.Contracts;

/// <summary>Статусы брони (ТЗ §5, BOOK-5).</summary>
public enum BookingStatus
{
    Pending,
    AwaitingPayment,
    Confirmed,
    CheckedIn,
    CheckedOut,
    Cancelled,
    NoShow,
    Expired,
}

/// <param name="OccupiedUntil">Номер сейчас по брони — до этого момента (выезд гостя).</param>
/// <param name="NextStartsAt">Начало ближайшей брони сегодня.</param>
public sealed record RoomTodayState(DateTimeOffset? OccupiedUntil, DateTimeOffset? NextStartsAt);

/// <summary>Запросы других модулей к бронированиям (номерной фонд, позже — уборка, финансы).</summary>
public interface IRoomBookingQueries
{
    /// <summary>
    /// Занятость номеров на сегодня по действующим броням: кто занят сейчас (до какого времени)
    /// и когда ближайшая бронь до конца дня. Номера без броней в словарь не попадают.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, RoomTodayState>> TodayStatesAsync(DateTimeOffset now, DateTimeOffset dayEnd, CancellationToken ct);

    /// <summary>Есть ли у номера хоть одна бронь (тогда номер нельзя удалить — только архивировать).</summary>
    Task<bool> HasAnyBookingAsync(Guid roomId, CancellationToken ct);

    /// <summary>Есть ли действующие брони с выездом позже указанной даты (номер нельзя убрать из продажи).</summary>
    Task<bool> HasUpcomingBookingsAsync(Guid roomId, DateOnly today, CancellationToken ct);
}

/// <summary>Бронь в карточке гостя. Время — по часам гостиницы «yyyy-MM-ddTHH:mm».</summary>
public sealed record GuestBookingItem(
    Guid BookingId,
    string Number,
    BookingStatus Status,
    string Kind,
    string StartsAt,
    string EndsAt,
    string RoomNumber,
    decimal TotalAmount,
    DateTimeOffset CreatedAt);

/// <param name="BookingsCount">Все брони гостя.</param>
/// <param name="VisitsCount">Посещения: брони, по которым гость заселялся (CheckedIn, CheckedOut).</param>
/// <param name="BookedAmount">Сумма действующих и завершённых броней (без отменённых, неявок и истёкших). Оплаты — с модулем Finance.</param>
/// <param name="LastStayAt">Начало последней действующей или завершённой брони.</param>
public sealed record GuestBookingStats(int BookingsCount, int VisitsCount, decimal BookedAmount, DateTimeOffset? LastStayAt);

/// <summary>Запросы модуля Guests к бронированиям: история, цифры по гостю, обезличивание.</summary>
public interface IGuestBookingQueries
{
    Task<IReadOnlyList<GuestBookingItem>> ListAsync(Guid guestId, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, GuestBookingStats>> StatsAsync(IReadOnlyCollection<Guid> guestIds, CancellationToken ct);

    /// <summary>Удаляет персональные данные гостя из броней и их истории. Выполняется сразу — вызывайте внутри транзакции.</summary>
    Task AnonymizeAsync(Guid guestId, string placeholderName, CancellationToken ct);
}

/// <summary>
/// Начисление по брони для финансов. К оплате (<see cref="Due"/>): действующая и завершённая бронь — её стоимость;
/// отменённая и неявка — начисленный штраф; истёкшая — ничего.
/// </summary>
/// <param name="StartsAt">Начало проживания, UTC.</param>
public sealed record BookingCharge(
    Guid BookingId,
    string Number,
    BookingStatus Status,
    string Kind,
    Guid? GuestId,
    string GuestName,
    string? RoomNumber,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    decimal TotalAmount,
    decimal PenaltyAmount)
{
    public decimal Due => Status switch
    {
        BookingStatus.Cancelled or BookingStatus.NoShow => PenaltyAmount,
        BookingStatus.Expired => 0m,
        _ => TotalAmount,
    };
}

/// <summary>Начисления по броням для модуля Finance (баланс, долги).</summary>
public interface IBookingCharges
{
    Task<BookingCharge?> GetAsync(Guid bookingId, CancellationToken ct);

    Task<IReadOnlyList<BookingCharge>> ListAsync(IReadOnlyCollection<Guid> bookingIds, CancellationToken ct);

    /// <summary>Все брони гостя (кабинет: свои платежи и возвраты).</summary>
    Task<IReadOnlyList<BookingCharge>> ListForGuestAsync(Guid guestId, CancellationToken ct);

    /// <summary>Брони, по которым есть что платить (стоимость или штраф больше нуля).</summary>
    Task<IReadOnlyList<BookingCharge>> ListWithDueAsync(CancellationToken ct);
}

/// <summary>
/// Бронь для отчётов (модуль Reporting): суммы, время, кто создал/заселил/выселил/отменил. Время — UTC.
/// Без персональных данных гостя: только <see cref="GuestId"/>.
/// </summary>
/// <param name="Kind">Hourly | Nightly.</param>
/// <param name="Source">Admin | Web.</param>
public sealed record BookingFact(
    Guid BookingId,
    string Number,
    string Kind,
    string Source,
    BookingStatus Status,
    Guid RoomId,
    Guid RoomTypeId,
    Guid? GuestId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int? Hours,
    int Nights,
    decimal AccommodationAmount,
    decimal DiscountAmount,
    decimal ServicesAmount,
    decimal ExtensionsAmount,
    decimal EarlyDepartureCredit,
    decimal TotalAmount,
    decimal PenaltyAmount,
    DateTimeOffset CreatedAt,
    Guid? CreatedBy,
    DateTimeOffset? CancelledAt,
    Guid? CancelledBy,
    DateTimeOffset? ActualCheckInAt,
    Guid? CheckedInBy,
    DateTimeOffset? ActualCheckOutAt,
    Guid? CheckedOutBy,
    decimal RoomChangeAmount = 0m,
    IReadOnlyList<BookingRoomSegment>? Segments = null);

/// <summary>Отрезок проживания в одном номере (после переселения у брони их несколько). Время — UTC.</summary>
public sealed record BookingRoomSegment(Guid RoomId, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

/// <summary>Факты броней для отчётов и дашбордов (только чтение).</summary>
public interface IBookingFacts
{
    /// <summary>
    /// Брони, затрагивающие период [from, to): проживание пересекается с периодом, создана, отменена,
    /// заселена или выселена в периоде.
    /// </summary>
    Task<IReadOnlyList<BookingFact>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    /// <summary>Категория номера по броням (фильтр отчётов по категории для платежей прошлых броней).</summary>
    Task<IReadOnlyDictionary<Guid, Guid>> RoomTypesAsync(IReadOnlyCollection<Guid> bookingIds, CancellationToken ct);

    /// <summary>Брони, ждущие подтверждения (с сайта и без подтверждения), которые ещё не закончились.</summary>
    Task<int> PendingCountAsync(DateTimeOffset now, CancellationToken ct);
}

/// <summary>Бронь для текста уведомления гостю (модуль Messaging). Время — UTC; номер — текущий (после переселения — последний).</summary>
/// <param name="Kind">Hourly | Nightly.</param>
public sealed record BookingNotice(
    Guid BookingId,
    string Number,
    BookingStatus Status,
    string Kind,
    Guid? GuestId,
    string GuestName,
    Guid RoomId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    decimal TotalAmount,
    decimal PenaltyAmount,
    string? CancelReason,
    DateTimeOffset CreatedAt);

/// <summary>Брони для уведомлений и напоминаний (только чтение).</summary>
public interface IBookingNotices
{
    Task<BookingNotice?> GetAsync(Guid bookingId, CancellationToken ct);

    /// <summary>Подтверждённые брони с началом в [from, to) — напоминание о заезде.</summary>
    Task<IReadOnlyList<BookingNotice>> StartingAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    /// <summary>Заселённые брони с окончанием в [from, to) — напоминание о выезде.</summary>
    Task<IReadOnlyList<BookingNotice>> EndingAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
