namespace Hms.Modules.Hotel.Contracts;

/// <summary>Правила отмены и штрафы (настраивает владелец, docs/development/development-roadmap.md §6.1).</summary>
public sealed record CancellationPolicySnapshot(
    int FreeCancelHoursBeforeCheckIn,
    int LateCancelPenaltyNights,
    int NoShowPenaltyNights,
    int EarlyDeparturePenaltyNights);

/// <summary>Правила почасовых броней (настраивает владелец, roadmap §6.1a).</summary>
public sealed record HourlyPolicySnapshot(
    int FreeCancelMinutesBeforeStart,
    int LateCancelPenaltyHours,
    int NoShowAfterMinutes,
    int NoShowPenaltyHours,
    int PendingHoldMinutes,
    bool RefundUnusedOnEarlyDeparture);

/// <summary>Настройки гостиницы, нужные другим модулям (бронирования, Identity, уборка).</summary>
public sealed record HotelSettingsSnapshot(
    Guid HotelId,
    string HotelName,
    string TimeZone,
    string Currency,
    TimeOnly CheckInTime,
    TimeOnly CheckOutTime,
    bool OnlinePaymentEnabled,
    int PrepaymentPercent,
    int PaymentHoldMinutes,
    int PendingHoldHours,
    bool AutoConfirmWebBookings,
    bool AutoReleaseRoomAfterCleaning,
    bool RequireEmailConfirmation,
    TimeOnly NoShowCutoffTime,
    int HourlyMinHours,
    int HourlyMaxHours,
    int CleaningBufferMinutes,
    CancellationPolicySnapshot CancellationPolicy,
    HourlyPolicySnapshot HourlyPolicy);

/// <summary>
/// Текущая гостиница и её настройки. Сейчас гостиница одна (ТЗ §2), модель данных готова к нескольким:
/// все бизнес-таблицы содержат HotelId, который модули берут отсюда.
/// </summary>
public interface IHotelContext
{
    Task<Guid> GetHotelIdAsync(CancellationToken ct);

    Task<HotelSettingsSnapshot> GetSettingsAsync(CancellationToken ct);
}
