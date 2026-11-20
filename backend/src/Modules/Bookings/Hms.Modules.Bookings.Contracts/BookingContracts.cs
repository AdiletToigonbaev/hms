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

/// <summary>Запросы других модулей к бронированиям (номерной фонд, позже — уборка, финансы).</summary>
public interface IRoomBookingQueries
{
    /// <summary>Номера, на которые в указанный день заезд по действующей брони (статус отображения «Бронь сегодня»).</summary>
    Task<IReadOnlySet<Guid>> RoomsWithArrivalOnAsync(DateOnly date, CancellationToken ct);

    /// <summary>Есть ли у номера хоть одна бронь (тогда номер нельзя удалить — только архивировать).</summary>
    Task<bool> HasAnyBookingAsync(Guid roomId, CancellationToken ct);

    /// <summary>Есть ли действующие брони с выездом позже указанной даты (номер нельзя убрать из продажи).</summary>
    Task<bool> HasUpcomingBookingsAsync(Guid roomId, DateOnly today, CancellationToken ct);
}
