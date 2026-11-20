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

    /// <summary>Часовой пояс — Asia/Bishkek (ТЗ §2); изменение не предусмотрено.</summary>
    public string TimeZone { get; private set; }

    public string Currency { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public void Update(string name, string? address, string? phone, string? email, string? description, DateTimeOffset now)
    {
        Name = name.Trim();
        Address = Normalize(address);
        Phone = Normalize(phone);
        Email = Normalize(email)?.ToLowerInvariant();
        Description = Normalize(description);
        UpdatedAt = now;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Правила отмены. Значения по умолчанию — docs/development/development-roadmap.md §6.1.</summary>
public sealed record CancellationPolicy(
    int FreeCancelHoursBeforeCheckIn = 24,
    int LateCancelPenaltyNights = 1,
    int NoShowPenaltyNights = 1,
    int EarlyDeparturePenaltyNights = 0);

/// <summary>Настройки гостиницы (одна строка на гостиницу).</summary>
public sealed class HotelSettings
{
    private HotelSettings()
    {
        Locale = "ru";
        CancellationPolicy = new CancellationPolicy();
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

    public CancellationPolicy CancellationPolicy { get; private set; }

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
        CancellationPolicy = change.CancellationPolicy;
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
    CancellationPolicy CancellationPolicy,
    string? Rules);
