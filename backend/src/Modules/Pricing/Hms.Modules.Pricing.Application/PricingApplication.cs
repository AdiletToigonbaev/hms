using FluentValidation;
using Hms.SharedKernel;

namespace Hms.Modules.Pricing.Application;

/// <param name="DaysOfWeek">Маска: Пн=1, Вт=2, Ср=4, Чт=8, Пт=16, Сб=32, Вс=64; все дни — 127.</param>
public sealed record RateDto(Guid Id, Guid RoomTypeId, string Name, DateOnly DateFrom, DateOnly DateTo, int DaysOfWeek, decimal Price, int Priority, bool IsActive);

public sealed record SaveRateRequest(Guid RoomTypeId, string Name, DateOnly DateFrom, DateOnly DateTo, int DaysOfWeek, decimal Price, int Priority, bool IsActive);

public sealed record DiscountDto(Guid Id, string Name, string? Code, string Type, decimal Value, DateOnly? ValidFrom, DateOnly? ValidTo, int MinNights, bool IsActive);

/// <param name="Type">Percent или Amount.</param>
public sealed record SaveDiscountRequest(string Name, string? Code, string Type, decimal Value, DateOnly? ValidFrom, DateOnly? ValidTo, int MinNights);

public sealed record ServiceDto(Guid Id, string Name, string? Description, decimal Price, bool IsPerNight, bool IsPublic, bool IsActive);

public sealed record SaveServiceRequest(string Name, string? Description, decimal Price, bool IsPerNight, bool IsPublic);

public interface IPricingAdminService
{
    Task<IReadOnlyList<RateDto>> ListRatesAsync(Guid? roomTypeId, CancellationToken ct);

    Task<Result<RateDto>> SaveRateAsync(Guid? id, SaveRateRequest request, CancellationToken ct);

    Task<Result> DeleteRateAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<DiscountDto>> ListDiscountsAsync(CancellationToken ct);

    Task<Result<DiscountDto>> SaveDiscountAsync(Guid? id, SaveDiscountRequest request, CancellationToken ct);

    Task<Result<DiscountDto>> SetDiscountActiveAsync(Guid id, bool active, CancellationToken ct);

    Task<IReadOnlyList<ServiceDto>> ListServicesAsync(bool publicOnly, CancellationToken ct);

    Task<Result<ServiceDto>> SaveServiceAsync(Guid? id, SaveServiceRequest request, CancellationToken ct);

    Task<Result<ServiceDto>> SetServiceActiveAsync(Guid id, bool active, CancellationToken ct);
}

public static class PricingAdminErrors
{
    public static readonly Error RateNotFound = Error.NotFound("rate.not_found", "Тариф не найден.");
    public static readonly Error DiscountNotFound = Error.NotFound("discount.not_found", "Скидка не найдена.");
    public static readonly Error ServiceNotFound = Error.NotFound("service.not_found", "Услуга не найдена.");
    public static readonly Error RoomTypeNotFound = Error.Validation("rate.room_type_not_found", "Категория номеров не найдена.");
    public static readonly Error CodeTaken = Error.Conflict("discount.code_taken", "Такой промокод уже есть.");
}

internal static class PriceRules
{
    public static bool IsMoney(decimal v) => v > 0 && v <= Money.MaxAbsoluteAmount && decimal.Round(v, 2) == v;
}

public sealed class SaveRateRequestValidator : AbstractValidator<SaveRateRequest>
{
    public SaveRateRequestValidator()
    {
        RuleFor(x => x.RoomTypeId).NotEmpty().WithMessage("Выберите категорию.");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Укажите название тарифа.").MaximumLength(100);
        RuleFor(x => x.DateTo).GreaterThanOrEqualTo(x => x.DateFrom).WithMessage("Конец периода не раньше начала.");
        RuleFor(x => x).Must(x => x.DateTo.DayNumber - x.DateFrom.DayNumber <= 730).WithName("dateTo").WithMessage("Период — не более двух лет.");
        RuleFor(x => x.DaysOfWeek).InclusiveBetween(1, 127).WithMessage("Выберите хотя бы один день недели.");
        RuleFor(x => x.Price).Must(PriceRules.IsMoney).WithMessage("Цена — больше нуля, не более двух знаков после запятой.");
        RuleFor(x => x.Priority).InclusiveBetween(0, 100).WithMessage("Приоритет — от 0 до 100.");
    }
}

public sealed class SaveDiscountRequestValidator : AbstractValidator<SaveDiscountRequest>
{
    public SaveDiscountRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Укажите название скидки.").MaximumLength(100);
        RuleFor(x => x.Code).Matches("^[A-Za-z0-9_-]{3,30}$").When(x => !string.IsNullOrWhiteSpace(x.Code))
            .WithMessage("Промокод: 3–30 латинских букв, цифр, «_» или «-».");
        RuleFor(x => x.Type).Must(t => t is "Percent" or "Amount").WithMessage("Тип: процент или сумма.");
        RuleFor(x => x.Value).InclusiveBetween(0.01m, 100m).When(x => x.Type == "Percent").WithMessage("Процент — от 0,01 до 100.");
        RuleFor(x => x.Value).Must(PriceRules.IsMoney).When(x => x.Type == "Amount").WithMessage("Сумма — больше нуля, не более двух знаков после запятой.");
        RuleFor(x => x.ValidTo).GreaterThanOrEqualTo(x => x.ValidFrom).When(x => x.ValidFrom is not null && x.ValidTo is not null)
            .WithMessage("Конец периода не раньше начала.");
        RuleFor(x => x.MinNights).InclusiveBetween(0, 365);
    }
}

public sealed class SaveServiceRequestValidator : AbstractValidator<SaveServiceRequest>
{
    public SaveServiceRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Укажите название услуги.").MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.Price).Must(PriceRules.IsMoney).WithMessage("Цена — больше нуля, не более двух знаков после запятой.");
    }
}
