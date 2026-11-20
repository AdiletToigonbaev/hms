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

/// <summary>Часы почасовой брони с одной ценой: [From, To) по времени гостиницы.</summary>
public sealed record HourPrice(DateTime From, DateTime To, int Hours, decimal UnitPrice, decimal Amount, string Source);

/// <summary>
/// Расчёт: цены по ночам (посуточно) или по часам (почасово), скидка (только на проживание), услуги, итог.
/// Все суммы — KGS, 2 знака. Старые снимки без Kind/Hours читаются как посуточные.
/// </summary>
public sealed record PriceQuote(
    IReadOnlyList<NightPrice> Nights,
    decimal AccommodationTotal,
    Guid? DiscountId,
    string? DiscountName,
    decimal DiscountAmount,
    IReadOnlyList<ServiceCharge> Services,
    decimal ServicesTotal,
    decimal Total,
    string Kind = PriceQuote.NightlyKind,
    IReadOnlyList<HourPrice>? Hours = null)
{
    public const string NightlyKind = "Nightly";
    public const string HourlyKind = "Hourly";
}

/// <summary>Запрос расчёта почасовой брони.</summary>
/// <param name="StartLocal">Начало по времени гостиницы (целый час).</param>
/// <param name="HourlyPrice">Цена часа категории.</param>
/// <param name="RoomHourlyOverride">Цена часа номера (если задана — вместо цены категории).</param>
public sealed record HourlyPriceRequest(
    Guid RoomTypeId,
    decimal HourlyPrice,
    decimal? RoomHourlyOverride,
    DateTime StartLocal,
    int Hours,
    string? PromoCode,
    IReadOnlyList<ServiceLine> Services);

/// <summary>Расчёт стоимости — только на backend (ТЗ §5, BOOK-4).</summary>
public interface IPriceCalculator
{
    Task<Result<PriceQuote>> QuoteAsync(PriceRequest request, CancellationToken ct);

    Task<Result<PriceQuote>> QuoteHourlyAsync(HourlyPriceRequest request, CancellationToken ct);
}
