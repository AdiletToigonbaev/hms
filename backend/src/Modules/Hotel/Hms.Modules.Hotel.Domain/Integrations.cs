namespace Hms.Modules.Hotel.Domain;

public enum IntegrationProvider
{
    MBank,
    Optima,
    WhatsApp,
    Email,
}

public enum IntegrationMode
{
    Sandbox,
    Production,
}

/// <summary>
/// Настройки интеграции (ТЗ §3.1). Здесь только несекретные параметры; ключи, токены и пароли —
/// исключительно в переменных окружения и в БД не попадают.
/// </summary>
public sealed class IntegrationSetting
{
    /// <summary>Несекретные параметры, которые владелец может указать для провайдера.</summary>
    public static readonly IReadOnlyDictionary<IntegrationProvider, string[]> AllowedPublicKeys = new Dictionary<IntegrationProvider, string[]>
    {
        [IntegrationProvider.MBank] = ["merchantId"],
        [IntegrationProvider.Optima] = ["merchantId"],
        [IntegrationProvider.WhatsApp] = ["phoneNumberId", "wabaId", "businessPhone"],
        [IntegrationProvider.Email] = [],
    };

    private IntegrationSetting()
    {
        PublicConfig = new Dictionary<string, string>();
    }

    public IntegrationSetting(Guid hotelId, IntegrationProvider provider, DateTimeOffset now)
        : this()
    {
        Id = Guid.CreateVersion7();
        HotelId = hotelId;
        Provider = provider;
        Mode = IntegrationMode.Sandbox;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid HotelId { get; private set; }

    public IntegrationProvider Provider { get; private set; }

    public bool IsEnabled { get; private set; }

    public IntegrationMode Mode { get; private set; }

    public Dictionary<string, string> PublicConfig { get; private set; }

    public DateTimeOffset? LastCheckAt { get; private set; }

    public string? LastCheckStatus { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(bool isEnabled, IntegrationMode mode, IReadOnlyDictionary<string, string> publicConfig, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(publicConfig);
        IsEnabled = isEnabled;
        Mode = mode;
        PublicConfig = publicConfig
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value.Trim(), StringComparer.Ordinal);
        UpdatedAt = now;
    }

    public void RecordCheck(string status, DateTimeOffset now)
    {
        LastCheckStatus = status;
        LastCheckAt = now;
    }
}
