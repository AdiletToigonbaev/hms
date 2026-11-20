namespace Hms.Modules.Rooms.Contracts;

/// <summary>
/// Хранимый статус номера (ТЗ §4). <c>Reserved</c> не хранится: это статус отображения
/// (свободный номер, на который сегодня заезд по брони) — вычисляется с Sprint 3 (модуль Bookings).
/// </summary>
public enum RoomStatus
{
    Available,
    Occupied,
    Cleaning,
    Maintenance,
    OutOfService,
}

/// <summary>Откуда пришла смена статуса — определяет, какие переходы разрешены.</summary>
public enum RoomStatusSource
{
    /// <summary>Администратор вручную (с обязательной причиной).</summary>
    Manual,

    /// <summary>Заселение (FrontDesk, Sprint 7).</summary>
    CheckIn,

    /// <summary>Выселение (FrontDesk, Sprint 7).</summary>
    CheckOut,

    /// <summary>Уборка проверена / завершена при авто-освобождении (Housekeeping, Sprint 7).</summary>
    Housekeeping,
}
