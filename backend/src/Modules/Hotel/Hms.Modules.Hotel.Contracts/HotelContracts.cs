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
    HourlyPolicySnapshot HourlyPolicy,
    bool AutoNoShowEnabled = false,
    int CleaningBufferNightlyMinutes = 30);

/// <summary>
/// Текущая гостиница и её настройки. Сейчас гостиница одна (ТЗ §2), модель данных готова к нескольким:
/// все бизнес-таблицы содержат HotelId, который модули берут отсюда.
/// </summary>
public interface IHotelContext
{
    Task<Guid> GetHotelIdAsync(CancellationToken ct);

    Task<HotelSettingsSnapshot> GetSettingsAsync(CancellationToken ct);
}

/// <summary>Контакты и правила гостиницы — для текстов сообщений и автоответчика.</summary>
public sealed record HotelProfile(
    string Name,
    string? Address,
    string? Phone,
    string? Email,
    string? Rules,
    TimeOnly CheckInTime,
    TimeOnly CheckOutTime,
    int HourlyMinHours);

/// <summary>Настройка интеграции WhatsApp, заданная владельцем (без секретов).</summary>
public sealed record WhatsAppIntegrationSettings(bool IsEnabled, string Mode, string? PhoneNumberId, string? WabaId, string? BusinessPhone);

/// <summary>Профиль гостиницы и интеграции для модуля Messaging.</summary>
public interface IHotelProfile
{
    Task<HotelProfile> GetAsync(CancellationToken ct);

    Task<WhatsAppIntegrationSettings> GetWhatsAppAsync(CancellationToken ct);
}

/// <summary>
/// Проверка соединения с WhatsApp Cloud API (реализует модуль Messaging). Hotel вызывает её из «Проверить соединение».
/// </summary>
public interface IWhatsAppConnectionCheck
{
    /// <summary>Заданы ли ключи на сервере (значения не раскрываются).</summary>
    bool SecretsConfigured { get; }

    /// <summary>Null — соединение работает; иначе текст ошибки для владельца.</summary>
    Task<string?> CheckAsync(string phoneNumberId, CancellationToken ct);
}
