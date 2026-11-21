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

/// <summary>Сведения о номере для других модулей (бронирования, уборка).</summary>
public sealed record RoomInfo(
    Guid Id,
    string Number,
    int? Floor,
    Guid RoomTypeId,
    string RoomTypeName,
    int Capacity,
    decimal BasePrice,
    decimal? PriceOverride,
    RoomStatus Status,
    bool IsArchived,
    bool RoomTypeArchived,
    decimal? HourlyBasePrice = null,
    decimal? HourlyPriceOverride = null,
    int? CleaningHourlyMinutes = null,
    int? CleaningNightlyMinutes = null)
{
    /// <summary>Продаётся ли номер по часам: задана цена часа у номера или категории.</summary>
    public bool SellsHourly => (HourlyPriceOverride ?? HourlyBasePrice) is not null;

    /// <summary>Можно продавать: номер и категория не в архиве, номер не «вне продажи».</summary>
    public bool IsSellable => !IsArchived && !RoomTypeArchived && Status != RoomStatus.OutOfService;
}

/// <summary>Чтение номерного фонда другими модулями — без доступа к таблицам Rooms.</summary>
public interface IRoomDirectory
{
    Task<RoomInfo?> GetAsync(Guid roomId, CancellationToken ct);

    /// <summary>Номера в продаже (опционально — одной категории).</summary>
    Task<IReadOnlyList<RoomInfo>> ListSellableAsync(Guid? roomTypeId, CancellationToken ct);

    /// <summary>Все номера не в архиве, включая «вне продажи», — для календаря занятости.</summary>
    Task<IReadOnlyList<RoomInfo>> ListActiveAsync(CancellationToken ct);

    Task<bool> RoomTypeExistsAsync(Guid roomTypeId, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, RoomInfo>> GetManyAsync(IEnumerable<Guid> roomIds, CancellationToken ct);
}

/// <summary>
/// Смена статуса номера другими модулями (заселение, выселение, уборка) — в той же единице работы, что и их изменения:
/// запись в историю статусов и аудит добавляются в контекст, сохраняет вызывающий модуль.
/// </summary>
public interface IRoomStatusChanger
{
    /// <summary>Сменить статус по правилам переходов для источника. Ошибка — если переход запрещён.</summary>
    Task<Hms.SharedKernel.Result> ChangeAsync(Guid roomId, RoomStatus target, RoomStatusSource source, string? reason, Guid? bookingId, CancellationToken ct);

    /// <summary>Текущий статус номера (null — номера нет).</summary>
    Task<RoomStatus?> GetStatusAsync(Guid roomId, CancellationToken ct);
}
