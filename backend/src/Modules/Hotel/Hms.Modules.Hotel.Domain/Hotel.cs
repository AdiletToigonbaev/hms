namespace Hms.Modules.Hotel.Domain;

/// <summary>Гостиница.</summary>
public sealed class HotelInfo
{
    public const string DefaultTimeZone = "Asia/Bishkek";
    public const string DefaultCurrency = "KGS";

    private HotelInfo()
    {
        Name = string.Empty;
        TimeZone = DefaultTimeZone;
        Currency = DefaultCurrency;
    }

    public HotelInfo(Guid id, string name, DateTimeOffset now)
        : this()
    {
        Id = id;
        Name = name;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public string? Address { get; private set; }

    public string? Phone { get; private set; }

    public string? Email { get; private set; }

    public string? Description { get; private set; }

    /// <summary>WhatsApp для гостей (E.164): кнопка «Написать в WhatsApp» на сайте (ссылка wa.me). Может отличаться от телефона стойки.</summary>
    public string? WhatsAppPhone { get; private set; }

    /// <summary>Ссылка на карточку гостиницы в 2ГИС: кнопка «Открыть в 2ГИС» на сайте. Только https-адреса 2ГИС (<see cref="HotelLinks"/>).</summary>
    public string? TwoGisUrl { get; private set; }

    /// <summary>Часовой пояс — Asia/Bishkek (ТЗ §2); изменение не предусмотрено.</summary>
    public string TimeZone { get; private set; }

    public string Currency { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public void Update(
        string name, string? address, string? phone, string? email, string? description,
        string? whatsAppPhone, string? twoGisUrl, DateTimeOffset now)
    {
        Name = name.Trim();
        Address = Normalize(address);
        Phone = Normalize(phone);
        Email = Normalize(email)?.ToLowerInvariant();
        Description = Normalize(description);
        WhatsAppPhone = Normalize(whatsAppPhone);
        TwoGisUrl = Normalize(twoGisUrl);
        UpdatedAt = now;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Ссылки гостиницы для сайта.</summary>
public static class HotelLinks
{
    // Домены 2ГИС: карточка (2gis.kg, 2gis.ru, 2gis.kz, 2gis.com) и короткие ссылки «Поделиться» (go.2gis.com).
    private static readonly string[] TwoGisHosts = ["2gis.kg", "2gis.ru", "2gis.kz", "2gis.com"];

    /// <summary>
    /// Ссылка на 2ГИС: только https и только домены 2ГИС (с поддоменами). Ссылка попадает на публичный сайт —
    /// произвольный адрес (в т.ч. javascript:) не принимается.
    /// </summary>
    public static bool IsTwoGisUrl(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(uri.UserInfo)
        && TwoGisHosts.Any(h => uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Правила отмены. Значения по умолчанию — docs/development/development-roadmap.md §6.1.</summary>
public sealed record CancellationPolicy(
    int FreeCancelHoursBeforeCheckIn = 24,
    int LateCancelPenaltyNights = 0,
    int NoShowPenaltyNights = 0,
    int EarlyDeparturePenaltyNights = 0);

/// <summary>
/// Правила почасовых броней (решение заказчика 2026-11-21, значения по умолчанию — roadmap §6.1a).
/// Все значения настраивает владелец; в коде не зашиваются.
/// </summary>
/// <param name="FreeCancelMinutesBeforeStart">Бесплатная отмена не позже чем за столько минут до начала.</param>
/// <param name="LateCancelPenaltyHours">Штраф за позднюю отмену, часов (по цене часа брони).</param>
/// <param name="NoShowAfterMinutes">Через сколько минут после начала гость считается не приехавшим (номер освобождается).</param>
/// <param name="NoShowPenaltyHours">Штраф за неявку, часов.</param>
/// <param name="PendingHoldMinutes">Сколько неподтверждённая веб-бронь держит номер (но не дольше начала брони).</param>
/// <param name="RefundUnusedOnEarlyDeparture">Возвращать ли деньги за неиспользованные часы при досрочном выезде.</param>
public sealed record HourlyPolicy(
    int FreeCancelMinutesBeforeStart = 120,
    int LateCancelPenaltyHours = 0,
    int NoShowAfterMinutes = 30,
    int NoShowPenaltyHours = 0,
    int PendingHoldMinutes = 30,
    bool RefundUnusedOnEarlyDeparture = false);

/// <summary>Настройки гостиницы (одна строка на гостиницу).</summary>
public sealed class HotelSettings
{
    private HotelSettings()
    {
        Locale = "ru";
        CancellationPolicy = new CancellationPolicy();
        HourlyPolicy = new HourlyPolicy();
    }

    public HotelSettings(Guid hotelId, DateTimeOffset now)
        : this()
    {
        HotelId = hotelId;
        UpdatedAt = now;
    }

    public Guid HotelId { get; private set; }

    public TimeOnly CheckInTime { get; private set; } = new(14, 0);

    public TimeOnly CheckOutTime { get; private set; } = new(12, 0);

    /// <summary>Онлайн-оплата; включается только при подключённом эквайринге (после Sprint 13).</summary>
    public bool OnlinePaymentEnabled { get; private set; }

    public int PrepaymentPercent { get; private set; }

    /// <summary>Сколько ждать онлайн-оплату брони (AwaitingPayment), минут.</summary>
    public int PaymentHoldMinutes { get; private set; } = 30;

    /// <summary>Сколько веб-бронь в статусе Pending держит номер до подтверждения, часов.</summary>
    public int PendingHoldHours { get; private set; } = 24;

    public bool AutoConfirmWebBookings { get; private set; }

    /// <summary>Освобождать номер после завершения уборки без проверки администратором (ТЗ §3.3).</summary>
    public bool AutoReleaseRoomAfterCleaning { get; private set; }

    public bool RequireEmailConfirmation { get; private set; } = true;

    /// <summary>Время дня после даты заезда, после которого неявка фиксируется как No-Show.</summary>
    public TimeOnly NoShowCutoffTime { get; private set; } = new(12, 0);

    /// <summary>
    /// Отмечать неявку автоматически (фоновая задача): подтверждённая бронь без заселения после времени неявки.
    /// С Sprint 7 (заселение) включено по умолчанию для новых гостиниц; владелец может выключить.
    /// </summary>
    public bool AutoNoShowEnabled { get; private set; } = true;

    /// <summary>Почасовые брони: минимум часов.</summary>
    public int HourlyMinHours { get; private set; } = 2;

    /// <summary>Почасовые брони: максимум часов (дольше — посуточно).</summary>
    public int HourlyMaxHours { get; private set; } = 12;

    /// <summary>Сколько минут номер закрыт на уборку после выезда (почасовые и посуточные брони).</summary>
    public int CleaningBufferMinutes { get; private set; } = 30;

    /// <summary>Уборка после посуточной брони (полная смена белья, санузел), мин. У категории номера можно задать своё.</summary>
    public int CleaningBufferNightlyMinutes { get; private set; } = 30;

    public CancellationPolicy CancellationPolicy { get; private set; }

    public HourlyPolicy HourlyPolicy { get; private set; }

    public string? Rules { get; private set; }

    public string Locale { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public void Update(HotelSettingsChange change, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(change);
        CheckInTime = change.CheckInTime;
        CheckOutTime = change.CheckOutTime;
        PrepaymentPercent = change.PrepaymentPercent;
        PaymentHoldMinutes = change.PaymentHoldMinutes;
        PendingHoldHours = change.PendingHoldHours;
        AutoConfirmWebBookings = change.AutoConfirmWebBookings;
        AutoReleaseRoomAfterCleaning = change.AutoReleaseRoomAfterCleaning;
        RequireEmailConfirmation = change.RequireEmailConfirmation;
        NoShowCutoffTime = change.NoShowCutoffTime;
        HourlyMinHours = change.HourlyMinHours;
        HourlyMaxHours = change.HourlyMaxHours;
        CleaningBufferMinutes = change.CleaningBufferMinutes;
        CancellationPolicy = change.CancellationPolicy;
        HourlyPolicy = change.HourlyPolicy ?? HourlyPolicy;
        AutoNoShowEnabled = change.AutoNoShowEnabled ?? AutoNoShowEnabled;
        CleaningBufferNightlyMinutes = change.CleaningBufferNightlyMinutes ?? CleaningBufferNightlyMinutes;
        Rules = string.IsNullOrWhiteSpace(change.Rules) ? null : change.Rules.Trim();
        UpdatedAt = now;
    }
}

/// <summary>Изменяемые владельцем поля настроек (онлайн-оплата — отдельно, после подключения банков).</summary>
public sealed record HotelSettingsChange(
    TimeOnly CheckInTime,
    TimeOnly CheckOutTime,
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
    CancellationPolicy CancellationPolicy,
    string? Rules,
    HourlyPolicy? HourlyPolicy = null,
    bool? AutoNoShowEnabled = null,
    int? CleaningBufferNightlyMinutes = null);
