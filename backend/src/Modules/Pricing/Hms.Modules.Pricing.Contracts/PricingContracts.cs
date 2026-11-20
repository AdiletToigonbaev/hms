using Hms.SharedKernel;

namespace Hms.Modules.Pricing.Contracts;

public sealed record ServiceLine(Guid ServiceId, int Quantity);

/// <summary>Запрос расчёта стоимости проживания одного номера.</summary>
/// <param name="BasePrice">Базовая цена категории за ночь.</param>
/// <param name="RoomPriceOverride">Собственная цена номера (если задана — вместо базовой).</param>
public sealed record PriceRequest(
    Guid RoomTypeId,
    decimal BasePrice,
    decimal? RoomPriceOverride,
    DateRange Stay,
    string? PromoCode,
    IReadOnlyList<ServiceLine> Services);

/// <param name="Source">Откуда цена: название тарифа, «Цена номера» или «Базовая цена».</param>
public sealed record NightPrice(DateOnly Date, decimal Price, string Source);

public sealed record ServiceCharge(Guid ServiceId, string Name, int Quantity, decimal UnitPrice, bool PerNight, decimal Total);

/// <summary>Расчёт: цены по ночам, скидка (только на проживание), услуги, итог. Все суммы — KGS, 2 знака.</summary>
public sealed record PriceQuote(
    IReadOnlyList<NightPrice> Nights,
    decimal AccommodationTotal,
    Guid? DiscountId,
    string? DiscountName,
    decimal DiscountAmount,
    IReadOnlyList<ServiceCharge> Services,
    decimal ServicesTotal,
    decimal Total);

/// <summary>Расчёт стоимости — только на backend (ТЗ §5, BOOK-4).</summary>
public interface IPriceCalculator
{
    Task<Result<PriceQuote>> QuoteAsync(PriceRequest request, CancellationToken ct);
}
