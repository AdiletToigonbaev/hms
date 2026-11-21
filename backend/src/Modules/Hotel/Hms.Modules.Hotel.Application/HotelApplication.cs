using FluentValidation;
using Hms.SharedKernel;

namespace Hms.Modules.Hotel.Application;

public sealed record HotelDto(
    Guid Id,
    string Name,
    string? Address,
    string? Phone,
    string? Email,
    string? Description,
    string TimeZone,
    string Currency,
    uint Version);

public sealed record UpdateHotelRequest(string Name, string? Address, string? Phone, string? Email, string? Description, uint Version);

public sealed record CancellationPolicyDto(
    int FreeCancelHoursBeforeCheckIn,
    int LateCancelPenaltyNights,
    int NoShowPenaltyNights,
    int EarlyDeparturePenaltyNights);

/// <summary>Правила почасовых броней: отмена, неявка, удержание веб-брони, досрочный выезд.</summary>
public sealed record HourlyPolicyDto(
    int FreeCancelMinutesBeforeStart,
    int LateCancelPenaltyHours,
    int NoShowAfterMinutes,
    int NoShowPenaltyHours,
    int PendingHoldMinutes,
    bool RefundUnusedOnEarlyDeparture);

/// <param name="CheckInTime">Время заезда, «HH:mm».</param>
public sealed record HotelSettingsDto(
    string CheckInTime,
    string CheckOutTime,
    bool OnlinePaymentEnabled,
    int PrepaymentPercent,
    int PaymentHoldMinutes,
    int PendingHoldHours,
    bool AutoConfirmWebBookings,
    bool AutoReleaseRoomAfterCleaning,
    bool RequireEmailConfirmation,
    string NoShowCutoffTime,
    int HourlyMinHours,
    int HourlyMaxHours,
    int CleaningBufferMinutes,
    CancellationPolicyDto CancellationPolicy,
    string? Rules,
    uint Version,
    HourlyPolicyDto HourlyPolicy,
    bool AutoNoShowEnabled = false,
    int CleaningBufferNightlyMinutes = 30);

public sealed record UpdateHotelSettingsRequest(
    string CheckInTime,
    string CheckOutTime,
    int PrepaymentPercent,
    int PaymentHoldMinutes,
    int PendingHoldHours,
    bool AutoConfirmWebBookings,
    bool AutoReleaseRoomAfterCleaning,
    bool RequireEmailConfirmation,
    string NoShowCutoffTime,
    int HourlyMinHours,
    int HourlyMaxHours,
    int CleaningBufferMinutes,
    CancellationPolicyDto CancellationPolicy,
    string? Rules,
    uint Version,
    HourlyPolicyDto? HourlyPolicy = null,
    bool? AutoNoShowEnabled = null,
    int? CleaningBufferNightlyMinutes = null);

/// <summary>Публичные сведения для сайта (без внутренних настроек).</summary>
public sealed record PublicHotelInfoDto(
    string Name,
    string? Address,
    string? Phone,
    string? Email,
    string? Description,
    string CheckInTime,
    string CheckOutTime,
    string? Rules,
    int FreeCancelHoursBeforeCheckIn,
    int HourlyMinHours = 2,
    int HourlyMaxHours = 12);

public sealed record IntegrationDto(
    string Provider,
    string DisplayName,
    bool IsEnabled,
    string Mode,
    IReadOnlyDictionary<string, string> PublicConfig,
    IReadOnlyList<string> AllowedPublicKeys,
    bool AdapterAvailable,
    bool SecretsConfigured,
    string Availability,
    DateTimeOffset? LastCheckAt,
    string? LastCheckStatus);

public sealed record UpdateIntegrationRequest(bool IsEnabled, string Mode, IReadOnlyDictionary<string, string>? PublicConfig);

public interface IHotelService
{
    Task<HotelDto> GetAsync(CancellationToken ct);

    Task<Result<HotelDto>> UpdateAsync(UpdateHotelRequest request, CancellationToken ct);

    Task<HotelSettingsDto> GetSettingsAsync(CancellationToken ct);

    Task<Result<HotelSettingsDto>> UpdateSettingsAsync(UpdateHotelSettingsRequest request, CancellationToken ct);

    Task<PublicHotelInfoDto> GetPublicInfoAsync(CancellationToken ct);
}

public interface IIntegrationService
{
    Task<IReadOnlyList<IntegrationDto>> ListAsync(CancellationToken ct);

    Task<Result<IntegrationDto>> GetAsync(string provider, CancellationToken ct);

    Task<Result<IntegrationDto>> UpdateAsync(string provider, UpdateIntegrationRequest request, CancellationToken ct);

    /// <summary>Проверка соединения. Для провайдеров без адаптера — ошибка «недоступно».</summary>
    Task<Result<IntegrationDto>> TestAsync(string provider, CancellationToken ct);
}

public static class HotelErrors
{
    public static readonly Error ConcurrencyConflict = Error.Conflict("concurrency.conflict", "Данные изменил другой пользователь. Обновите страницу и повторите.");
    public static readonly Error ProviderNotFound = Error.NotFound("integration.not_found", "Интеграция не найдена.");
    public static readonly Error AdapterNotAvailable = Error.BusinessRule("integration.not_available", "Интеграция ещё не подключена: нужны официальная документация и доступы провайдера.");
    public static readonly Error SecretsMissing = Error.BusinessRule("integration.secrets_missing", "Не заданы ключи доступа в переменных окружения сервера.");
    public static readonly Error OwnerEmailMissing = Error.BusinessRule("integration.no_recipient", "У вашей учётной записи не указан email для тестового письма.");

    public static Error PublicKeyNotAllowed(string key) =>
        Error.Validation("integration.key_not_allowed", $"Параметр «{key}» нельзя хранить в настройках. Секреты задаются только в переменных окружения.");
}

internal static class TimeText
{
    public static bool IsValid(string? value) =>
        TimeOnly.TryParseExact(value, "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _);
}

public sealed class UpdateHotelRequestValidator : AbstractValidator<UpdateHotelRequest>
{
    public UpdateHotelRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Укажите название гостиницы.").MaximumLength(150);
        RuleFor(x => x.Address).MaximumLength(300);
        RuleFor(x => x.Phone).Must(v => PhoneNumber.Parse(v).IsSuccess).When(x => !string.IsNullOrWhiteSpace(x.Phone))
            .WithMessage("Некорректный номер телефона. Пример: +996 555 123 456.");
        RuleFor(x => x.Email).EmailAddress().WithMessage("Некорректный email.").MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Description).MaximumLength(2000);
    }
}

public sealed class UpdateHotelSettingsRequestValidator : AbstractValidator<UpdateHotelSettingsRequest>
{
    public UpdateHotelSettingsRequestValidator()
    {
        RuleFor(x => x.CheckInTime).Must(TimeText.IsValid).WithMessage("Время в формате ЧЧ:ММ.");
        RuleFor(x => x.CheckOutTime).Must(TimeText.IsValid).WithMessage("Время в формате ЧЧ:ММ.");
        RuleFor(x => x.NoShowCutoffTime).Must(TimeText.IsValid).WithMessage("Время в формате ЧЧ:ММ.");
        RuleFor(x => x.PrepaymentPercent).InclusiveBetween(0, 100).WithMessage("Предоплата — от 0 до 100%.");
        RuleFor(x => x.PaymentHoldMinutes).InclusiveBetween(5, 1440).WithMessage("От 5 минут до 24 часов.");
        RuleFor(x => x.PendingHoldHours).InclusiveBetween(1, 168).WithMessage("От 1 часа до 7 дней.");
        RuleFor(x => x.HourlyMinHours).InclusiveBetween(1, 24).WithMessage("Минимум часов — от 1 до 24.");
        RuleFor(x => x.HourlyMaxHours).InclusiveBetween(1, 24).WithMessage("Максимум часов — от 1 до 24.")
            .GreaterThanOrEqualTo(x => x.HourlyMinHours).WithMessage("Максимум часов не меньше минимума.");
        RuleFor(x => x.CleaningBufferMinutes).InclusiveBetween(0, 240).WithMessage("Уборка — от 0 до 240 минут.");
        RuleFor(x => x.CleaningBufferNightlyMinutes).InclusiveBetween(0, 240).When(x => x.CleaningBufferNightlyMinutes is not null).WithMessage("Уборка — от 0 до 240 минут.");
        RuleFor(x => x.CancellationPolicy).NotNull();
        RuleFor(x => x.CancellationPolicy.FreeCancelHoursBeforeCheckIn).InclusiveBetween(0, 720).When(x => x.CancellationPolicy is not null)
            .WithMessage("От 0 до 720 часов.");
        RuleFor(x => x.CancellationPolicy.LateCancelPenaltyNights).InclusiveBetween(0, 30).When(x => x.CancellationPolicy is not null)
            .WithMessage("От 0 до 30 ночей.");
        RuleFor(x => x.CancellationPolicy.NoShowPenaltyNights).InclusiveBetween(0, 30).When(x => x.CancellationPolicy is not null)
            .WithMessage("От 0 до 30 ночей.");
        RuleFor(x => x.CancellationPolicy.EarlyDeparturePenaltyNights).InclusiveBetween(0, 30).When(x => x.CancellationPolicy is not null)
            .WithMessage("От 0 до 30 ночей.");
        RuleFor(x => x.Rules).MaximumLength(5000);
        When(x => x.HourlyPolicy is not null, () =>
        {
            RuleFor(x => x.HourlyPolicy!.FreeCancelMinutesBeforeStart).InclusiveBetween(0, 10080).WithMessage("От 0 минут до 7 дней.");
            RuleFor(x => x.HourlyPolicy!.LateCancelPenaltyHours).InclusiveBetween(0, 24).WithMessage("От 0 до 24 часов.");
            RuleFor(x => x.HourlyPolicy!.NoShowAfterMinutes).InclusiveBetween(5, 720).WithMessage("От 5 минут до 12 часов.");
            RuleFor(x => x.HourlyPolicy!.NoShowPenaltyHours).InclusiveBetween(0, 24).WithMessage("От 0 до 24 часов.");
            RuleFor(x => x.HourlyPolicy!.PendingHoldMinutes).InclusiveBetween(5, 1440).WithMessage("От 5 минут до 24 часов.");
        });
    }
}

public sealed class UpdateIntegrationRequestValidator : AbstractValidator<UpdateIntegrationRequest>
{
    public UpdateIntegrationRequestValidator()
    {
        RuleFor(x => x.Mode).Must(m => m is "Sandbox" or "Production").WithMessage("Режим: Sandbox или Production.");
        RuleForEach(x => x.PublicConfig).ChildRules(kv =>
        {
            kv.RuleFor(p => p.Value).MaximumLength(200).WithMessage("Значение не длиннее 200 символов.");
        }).When(x => x.PublicConfig is not null);
    }
}
