using System.Globalization;
using Hms.Modules.Audit.Contracts;
using Hms.Modules.Hotel.Application;
using Hms.Modules.Hotel.Contracts;
using Hms.Modules.Hotel.Domain;
using Hms.Platform.Email;
using Hms.Platform.Security;
using Hms.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Hms.Modules.Hotel.Infrastructure;

internal sealed class HotelService(DbContext db, HotelContext context, IAuditTrail audit, TimeProvider time) : IHotelService
{
    public async Task<HotelDto> GetAsync(CancellationToken ct) => ToDto(await LoadHotelAsync(ct));

    public async Task<Result<HotelDto>> UpdateAsync(UpdateHotelRequest request, CancellationToken ct)
    {
        var hotel = await LoadHotelAsync(ct);
        var before = ToDto(hotel);
        db.Entry(hotel).Property(h => h.Version).OriginalValue = request.Version;

        var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : PhoneNumber.Parse(request.Phone).Value.Value;
        hotel.Update(request.Name, request.Address, phone, request.Email, request.Description, time.GetUtcNow());
        audit.Record(new AuditEntry("hotel.updated", "Hotel", hotel.Id.ToString(), before, ToDto(hotel)));

        if (!await TrySaveAsync(ct))
        {
            return HotelErrors.ConcurrencyConflict;
        }

        context.Invalidate();
        return ToDto(hotel);
    }

    public async Task<HotelSettingsDto> GetSettingsAsync(CancellationToken ct) => ToDto(await LoadSettingsAsync(ct));

    public async Task<Result<HotelSettingsDto>> UpdateSettingsAsync(UpdateHotelSettingsRequest r, CancellationToken ct)
    {
        var settings = await LoadSettingsAsync(ct);
        var before = ToDto(settings);
        db.Entry(settings).Property(s => s.Version).OriginalValue = r.Version;

        settings.Update(
            new HotelSettingsChange(
                ParseTime(r.CheckInTime),
                ParseTime(r.CheckOutTime),
                r.PrepaymentPercent,
                r.PaymentHoldMinutes,
                r.PendingHoldHours,
                r.AutoConfirmWebBookings,
                r.AutoReleaseRoomAfterCleaning,
                r.RequireEmailConfirmation,
                ParseTime(r.NoShowCutoffTime),
                r.HourlyMinHours,
                r.HourlyMaxHours,
                r.CleaningBufferMinutes,
                new CancellationPolicy(
                    r.CancellationPolicy.FreeCancelHoursBeforeCheckIn,
                    r.CancellationPolicy.LateCancelPenaltyNights,
                    r.CancellationPolicy.NoShowPenaltyNights,
                    r.CancellationPolicy.EarlyDeparturePenaltyNights),
                r.Rules,
                r.HourlyPolicy is { } h
                    ? new HourlyPolicy(h.FreeCancelMinutesBeforeStart, h.LateCancelPenaltyHours, h.NoShowAfterMinutes, h.NoShowPenaltyHours, h.PendingHoldMinutes, h.RefundUnusedOnEarlyDeparture)
                    : null,
                r.AutoNoShowEnabled),
            time.GetUtcNow());
        audit.Record(new AuditEntry("hotel.settings.updated", "HotelSettings", settings.HotelId.ToString(), before, ToDto(settings)));

        if (!await TrySaveAsync(ct))
        {
            return HotelErrors.ConcurrencyConflict;
        }

        context.Invalidate();
        return ToDto(settings);
    }

    public async Task<PublicHotelInfoDto> GetPublicInfoAsync(CancellationToken ct)
    {
        var hotel = await LoadHotelAsync(ct);
        var s = await LoadSettingsAsync(ct);
        return new PublicHotelInfoDto(
            hotel.Name, hotel.Address, hotel.Phone, hotel.Email, hotel.Description,
            FormatTime(s.CheckInTime), FormatTime(s.CheckOutTime), s.Rules, s.CancellationPolicy.FreeCancelHoursBeforeCheckIn,
            s.HourlyMinHours, s.HourlyMaxHours);
    }

    private async Task<HotelInfo> LoadHotelAsync(CancellationToken ct)
    {
        var id = await context.GetHotelIdAsync(ct);
        return await db.Set<HotelInfo>().FirstAsync(h => h.Id == id, ct);
    }

    private async Task<HotelSettings> LoadSettingsAsync(CancellationToken ct)
    {
        var id = await context.GetHotelIdAsync(ct);
        return await db.Set<HotelSettings>().FirstAsync(s => s.HotelId == id, ct);
    }

    private async Task<bool> TrySaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    private static TimeOnly ParseTime(string value) => TimeOnly.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture);

    internal static string FormatTime(TimeOnly value) => value.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static HotelDto ToDto(HotelInfo h) =>
        new(h.Id, h.Name, h.Address, h.Phone, h.Email, h.Description, h.TimeZone, h.Currency, h.Version);

    private static HotelSettingsDto ToDto(HotelSettings s) => new(
        FormatTime(s.CheckInTime),
        FormatTime(s.CheckOutTime),
        s.OnlinePaymentEnabled,
        s.PrepaymentPercent,
        s.PaymentHoldMinutes,
        s.PendingHoldHours,
        s.AutoConfirmWebBookings,
        s.AutoReleaseRoomAfterCleaning,
        s.RequireEmailConfirmation,
        FormatTime(s.NoShowCutoffTime),
        s.HourlyMinHours,
        s.HourlyMaxHours,
        s.CleaningBufferMinutes,
        new CancellationPolicyDto(
            s.CancellationPolicy.FreeCancelHoursBeforeCheckIn,
            s.CancellationPolicy.LateCancelPenaltyNights,
            s.CancellationPolicy.NoShowPenaltyNights,
            s.CancellationPolicy.EarlyDeparturePenaltyNights),
        s.Rules,
        s.Version,
        new HourlyPolicyDto(
            s.HourlyPolicy.FreeCancelMinutesBeforeStart,
            s.HourlyPolicy.LateCancelPenaltyHours,
            s.HourlyPolicy.NoShowAfterMinutes,
            s.HourlyPolicy.NoShowPenaltyHours,
            s.HourlyPolicy.PendingHoldMinutes,
            s.HourlyPolicy.RefundUnusedOnEarlyDeparture),
        s.AutoNoShowEnabled);
}

internal sealed class IntegrationService(
    DbContext db,
    IHotelContext context,
    IConfiguration configuration,
    IEmailOutbox emails,
    ICurrentUser currentUser,
    IAuditTrail audit,
    TimeProvider time) : IIntegrationService
{
    // Адаптер есть только у email (SMTP). Банки и WhatsApp — после официальной документации и доступов.
    private static readonly HashSet<IntegrationProvider> AdapterImplemented = [IntegrationProvider.Email];

    private static readonly string[] SecretWords = ["secret", "key", "token", "password", "pass", "pin"];

    public async Task<IReadOnlyList<IntegrationDto>> ListAsync(CancellationToken ct)
    {
        var hotelId = await context.GetHotelIdAsync(ct);
        var rows = await db.Set<IntegrationSetting>().AsNoTracking().Where(i => i.HotelId == hotelId).ToListAsync(ct);
        return rows.OrderBy(i => i.Provider).Select(ToDto).ToList();
    }

    public async Task<Result<IntegrationDto>> GetAsync(string provider, CancellationToken ct)
    {
        var row = await LoadAsync(provider, ct);
        return row is null ? HotelErrors.ProviderNotFound : ToDto(row);
    }

    public async Task<Result<IntegrationDto>> UpdateAsync(string provider, UpdateIntegrationRequest request, CancellationToken ct)
    {
        var row = await LoadAsync(provider, ct);
        if (row is null)
        {
            return HotelErrors.ProviderNotFound;
        }

        var config = request.PublicConfig ?? new Dictionary<string, string>();
        var allowed = IntegrationSetting.AllowedPublicKeys[row.Provider];
        foreach (var key in config.Keys)
        {
            if (!allowed.Contains(key, StringComparer.Ordinal) || SecretWords.Any(w => key.Contains(w, StringComparison.OrdinalIgnoreCase) && key != "merchantId"))
            {
                return HotelErrors.PublicKeyNotAllowed(key);
            }
        }

        if (request.IsEnabled && !row.IsEnabled)
        {
            if (!AdapterImplemented.Contains(row.Provider))
            {
                return HotelErrors.AdapterNotAvailable;
            }

            if (!SecretsConfigured(row.Provider))
            {
                return HotelErrors.SecretsMissing;
            }
        }

        var before = ToDto(row);
        row.Update(request.IsEnabled, Enum.Parse<IntegrationMode>(request.Mode), config, time.GetUtcNow());
        audit.Record(new AuditEntry("integration.updated", "Integration", row.Provider.ToString(), before, ToDto(row)));
        await db.SaveChangesAsync(ct);
        return ToDto(row);
    }

    public async Task<Result<IntegrationDto>> TestAsync(string provider, CancellationToken ct)
    {
        var row = await LoadAsync(provider, ct);
        if (row is null)
        {
            return HotelErrors.ProviderNotFound;
        }

        if (!AdapterImplemented.Contains(row.Provider))
        {
            return HotelErrors.AdapterNotAvailable;
        }

        if (!SecretsConfigured(row.Provider))
        {
            return HotelErrors.SecretsMissing;
        }

        // Email: тестовое письмо текущему пользователю через outbox (результат — во входящих / Mailpit).
        if (string.IsNullOrWhiteSpace(currentUser.Email))
        {
            return HotelErrors.OwnerEmailMissing;
        }

        emails.Enqueue(new EmailMessage(
            currentUser.Email,
            "Проверка отправки писем",
            "<p>Это тестовое письмо из системы управления гостиницей. Отправка работает.</p>",
            "Это тестовое письмо из системы управления гостиницей. Отправка работает."));
        row.RecordCheck("test_email_queued", time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return ToDto(row);
    }

    private async Task<IntegrationSetting?> LoadAsync(string provider, CancellationToken ct)
    {
        if (!Enum.TryParse<IntegrationProvider>(provider, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
        {
            return null;
        }

        var hotelId = await context.GetHotelIdAsync(ct);
        return await db.Set<IntegrationSetting>().FirstOrDefaultAsync(i => i.HotelId == hotelId && i.Provider == parsed, ct);
    }

    /// <summary>Заданы ли ключи в окружении. Значения не читаются в ответ и не логируются.</summary>
    private bool SecretsConfigured(IntegrationProvider provider) => provider switch
    {
        IntegrationProvider.Email => !string.IsNullOrWhiteSpace(configuration["Email:Host"]),
        _ => configuration.GetSection($"Integrations:{provider}").GetChildren().Any(c => !string.IsNullOrWhiteSpace(c.Value)),
    };

    private IntegrationDto ToDto(IntegrationSetting i)
    {
        var adapter = AdapterImplemented.Contains(i.Provider);
        var secrets = SecretsConfigured(i.Provider);
        var availability = !adapter ? "awaiting_access" : !secrets ? "secrets_missing" : i.IsEnabled ? "active" : "ready";
        return new IntegrationDto(
            i.Provider.ToString(),
            DisplayName(i.Provider),
            i.IsEnabled,
            i.Mode.ToString(),
            i.PublicConfig,
            IntegrationSetting.AllowedPublicKeys[i.Provider],
            adapter,
            secrets,
            availability,
            i.LastCheckAt,
            i.LastCheckStatus);
    }

    private static string DisplayName(IntegrationProvider p) => p switch
    {
        IntegrationProvider.MBank => "MBANK — интернет-эквайринг",
        IntegrationProvider.Optima => "Optima Bank — интернет-эквайринг",
        IntegrationProvider.WhatsApp => "WhatsApp Business Platform",
        IntegrationProvider.Email => "Email (SMTP)",
        _ => p.ToString(),
    };
}
