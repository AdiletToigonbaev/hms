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
