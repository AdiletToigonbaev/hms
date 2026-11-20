using Hms.Modules.Pricing.Contracts;
using Hms.SharedKernel;

namespace Hms.Modules.Pricing.Domain;

/// <summary>Дни недели битовой маской: Пн = 1, Вт = 2 … Вс = 64. Все дни = 127.</summary>
[Flags]
public enum WeekDays
{
    None = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 4,
    Thursday = 8,
    Friday = 16,
    Saturday = 32,
    Sunday = 64,
    Weekend = Friday | Saturday,
    All = 127,
}

public static class WeekDaysExtensions
{
    public static WeekDays ToFlag(this DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => WeekDays.Monday,
        DayOfWeek.Tuesday => WeekDays.Tuesday,
        DayOfWeek.Wednesday => WeekDays.Wednesday,
        DayOfWeek.Thursday => WeekDays.Thursday,
        DayOfWeek.Friday => WeekDays.Friday,
        DayOfWeek.Saturday => WeekDays.Saturday,
        _ => WeekDays.Sunday,
    };
}

/// <summary>
/// Тариф категории на период (RATE-2): сезон, праздники, цены выходных (по маске дней недели).
/// Цена ночи = тариф с наибольшим приоритетом, покрывающий дату; иначе цена номера или базовая цена категории.
/// </summary>
public sealed class RoomRate
{
    private RoomRate()
    {
        Name = string.Empty;
    }

    public RoomRate(Guid hotelId, Guid roomTypeId, string name, DateOnly from, DateOnly to, WeekDays days, decimal price, int priority, DateTimeOffset now)
        : this()
    {
        Id = Guid.CreateVersion7();
        HotelId = hotelId;
        RoomTypeId = roomTypeId;
        CreatedAt = now;
        Update(name, from, to, days, price, priority, true, now);
    }

    public Guid Id { get; private set; }

    public Guid HotelId { get; private set; }

    public Guid RoomTypeId { get; private set; }

    public string Name { get; private set; }

    /// <summary>Первая ночь периода (включительно).</summary>
    public DateOnly DateFrom { get; private set; }

    /// <summary>Последняя ночь периода (включительно).</summary>
    public DateOnly DateTo { get; private set; }

    public WeekDays DaysOfWeek { get; private set; }

    public decimal Price { get; private set; }

    public int Priority { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(string name, DateOnly from, DateOnly to, WeekDays days, decimal price, int priority, bool isActive, DateTimeOffset now)
    {
        Name = name.Trim();
        DateFrom = from;
        DateTo = to;
        DaysOfWeek = days;
        Price = Money.Kgs(price).Amount;
        Priority = priority;
        IsActive = isActive;
        UpdatedAt = now;
    }

    /// <summary>За что цена тарифа: за ночь (посуточно) или за час (почасовые брони).</summary>
    public RateUnit Unit { get; private set; } = RateUnit.Night;

    public void SetUnit(RateUnit unit) => Unit = unit;

    public bool AppliesTo(DateOnly night) =>
        IsActive && night >= DateFrom && night <= DateTo && DaysOfWeek.HasFlag(night.DayOfWeek.ToFlag());
}

public enum RateUnit
{
    Night,
    Hour,
}

public enum DiscountType
{
    Percent,
    Amount,
}

/// <summary>Скидка на проживание (RATE-3): процент или сумма, по промокоду или без, период заезда, минимум ночей.</summary>
public sealed class Discount
{
    private Discount()
    {
        Name = string.Empty;
    }

    public Discount(Guid hotelId, DateTimeOffset now)
        : this()
    {
        Id = Guid.CreateVersion7();
        HotelId = hotelId;
        IsActive = true;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid HotelId { get; private set; }

    public string Name { get; private set; }

    /// <summary>Промокод (верхний регистр). Без кода скидка применяется только администратором явно — в Sprint 3 скидки применяются по коду.</summary>
    public string? Code { get; private set; }

    public DiscountType Type { get; private set; }

    public decimal Value { get; private set; }

    /// <summary>Заезд не раньше этой даты.</summary>
    public DateOnly? ValidFrom { get; private set; }

    /// <summary>Заезд не позже этой даты.</summary>
    public DateOnly? ValidTo { get; private set; }

    public int MinNights { get; private set; }

    /// <summary>Минимум часов для почасовой брони (минимум ночей относится только к посуточным).</summary>
    public int MinHours { get; private set; }

    public void SetMinHours(int hours) => MinHours = hours;

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(string name, string? code, DiscountType type, decimal value, DateOnly? validFrom, DateOnly? validTo, int minNights, DateTimeOffset now)
    {
        Name = name.Trim();
        Code = string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();
        Type = type;
        Value = decimal.Round(value, 2, MidpointRounding.AwayFromZero);
        ValidFrom = validFrom;
        ValidTo = validTo;
        MinNights = minNights;
        UpdatedAt = now;
    }

    public void SetActive(bool active, DateTimeOffset now)
    {
        IsActive = active;
        UpdatedAt = now;
    }

    /// <summary>Применима ли скидка к проживанию (иначе — понятная причина).</summary>
    public Error? CheckApplicable(DateRange stay)
    {
        if (!IsActive)
        {
            return PricingErrors.PromoInactive;
        }

        if ((ValidFrom is { } from && stay.Start < from) || (ValidTo is { } to && stay.Start > to))
        {
            return PricingErrors.PromoOutOfPeriod;
        }

        return stay.Nights < MinNights ? PricingErrors.PromoMinNights(MinNights) : null;
    }

    /// <summary>Применимость к почасовой брони: период — по дате начала, условие — минимум часов.</summary>
    public Error? CheckApplicableHourly(DateOnly arrival, int hours)
    {
        if (!IsActive)
        {
            return PricingErrors.PromoInactive;
        }

        if ((ValidFrom is { } from && arrival < from) || (ValidTo is { } to && arrival > to))
        {
            return PricingErrors.PromoOutOfPeriod;
        }

        return hours < MinHours ? PricingErrors.PromoMinHours(MinHours) : null;
    }

    /// <summary>Сумма скидки: процент округляется до копеек (половина — от нуля), сумма не больше стоимости проживания.</summary>
    public decimal AmountFor(decimal accommodationTotal) => Type == DiscountType.Percent
        ? decimal.Round(accommodationTotal * Value / 100m, 2, MidpointRounding.AwayFromZero)
        : Math.Min(Value, accommodationTotal);
}

/// <summary>Дополнительная услуга (RATE-4): завтрак, доп. место, ранний заезд, трансфер…</summary>
public sealed class ExtraService
{
    private ExtraService()
    {
        Name = string.Empty;
    }

    public ExtraService(Guid hotelId, DateTimeOffset now)
        : this()
    {
        Id = Guid.CreateVersion7();
        HotelId = hotelId;
        IsActive = true;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid HotelId { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public decimal Price { get; private set; }

    /// <summary>Цена за каждую ночь (завтрак, доп. место) или разово (трансфер, поздний выезд).</summary>
    public bool IsPerNight { get; private set; }

    /// <summary>Можно ли заказать гостю на сайте (иначе — только администратор).</summary>
    public bool IsPublic { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(string name, string? description, decimal price, bool perNight, bool isPublic, DateTimeOffset now)
    {
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Price = Money.Kgs(price).Amount;
        IsPerNight = perNight;
        IsPublic = isPublic;
        UpdatedAt = now;
    }

    public void SetActive(bool active, DateTimeOffset now)
    {
        IsActive = active;
        UpdatedAt = now;
    }
}

public static class PricingErrors
{
    public static readonly Error PromoNotFound = Error.BusinessRule("pricing.promo_not_found", "Промокод не найден.");
    public static readonly Error PromoInactive = Error.BusinessRule("pricing.promo_inactive", "Промокод больше не действует.");
    public static readonly Error PromoOutOfPeriod = Error.BusinessRule("pricing.promo_out_of_period", "Промокод не действует на эти даты заезда.");
    public static readonly Error ServiceNotFound = Error.BusinessRule("pricing.service_unavailable", "Услуга недоступна.");

    public static Error PromoMinHours(int hours) =>
        Error.BusinessRule("pricing.promo_min_hours", $"Промокод действует при брони от {hours} ч.");

    public static Error PromoMinNights(int nights) =>
        Error.BusinessRule("pricing.promo_min_nights", $"Промокод действует при проживании от {nights} ночей.");
}

/// <summary>Расчёт стоимости — чистая функция без доступа к БД (тестируется отдельно).</summary>
public static class PriceCalculation
{
    public const string BasePriceSource = "Базовая цена";
    public const string RoomPriceSource = "Цена номера";

    public static PriceQuote Calculate(
        DateRange stay,
        decimal basePrice,
        decimal? roomPriceOverride,
        IReadOnlyList<RoomRate> rates,
        Discount? discount,
        IReadOnlyList<(ExtraService Service, int Quantity)> services)
    {
        ArgumentNullException.ThrowIfNull(rates);
        ArgumentNullException.ThrowIfNull(services);

        var nights = new List<NightPrice>(stay.Nights);
        foreach (var night in stay.EachNight())
        {
            var rate = rates
                .Where(r => r.Unit == RateUnit.Night && r.AppliesTo(night))
                .OrderByDescending(r => r.Priority)
                .ThenByDescending(r => r.CreatedAt)
                .FirstOrDefault();
            nights.Add(rate is not null
                ? new NightPrice(night, rate.Price, $"Тариф «{rate.Name}»")
                : roomPriceOverride is { } own
                    ? new NightPrice(night, own, RoomPriceSource)
                    : new NightPrice(night, basePrice, BasePriceSource));
        }

        var accommodation = nights.Sum(n => n.Price);
        var discountAmount = discount?.AmountFor(accommodation) ?? 0m;

        var charges = services
            .Where(s => s.Quantity > 0)
            .Select(s =>
            {
                var units = s.Service.IsPerNight ? s.Quantity * stay.Nights : s.Quantity;
                return new ServiceCharge(s.Service.Id, s.Service.Name, s.Quantity, s.Service.Price, s.Service.IsPerNight, Money.Kgs(s.Service.Price * units).Amount);
            })
            .ToList();
        var servicesTotal = charges.Sum(c => c.Total);

        return new PriceQuote(
            nights,
            accommodation,
            discount?.Id,
            discount?.Name,
            discountAmount,
            charges,
            servicesTotal,
            accommodation - discountAmount + servicesTotal);
    }

    public const string HourPriceSource = "Цена часа";
    public const string RoomHourPriceSource = "Цена часа номера";

    /// <summary>
    /// Почасовая бронь: каждый час — по тарифу «за час» с наибольшим приоритетом на дату и день недели этого часа,
    /// иначе цена часа номера, иначе цена часа категории. Подряд идущие часы с одной ценой объединяются в строку.
    /// Услуги «за ночь» в почасовой брони считаются один раз.
    /// </summary>
    public static PriceQuote CalculateHourly(
        DateTime startLocal,
        int hours,
        decimal hourlyPrice,
        decimal? roomHourlyOverride,
        IReadOnlyList<RoomRate> rates,
        Discount? discount,
        IReadOnlyList<(ExtraService Service, int Quantity)> services)
    {
        ArgumentNullException.ThrowIfNull(rates);
        ArgumentNullException.ThrowIfNull(services);

        var lines = new List<HourPrice>();
        for (var i = 0; i < hours; i++)
        {
            var from = startLocal.AddHours(i);
            var day = DateOnly.FromDateTime(from);
            var rate = rates
                .Where(r => r.Unit == RateUnit.Hour && r.AppliesTo(day))
                .OrderByDescending(r => r.Priority)
                .ThenByDescending(r => r.CreatedAt)
                .FirstOrDefault();
            var (price, source) = rate is not null
                ? (rate.Price, $"Тариф «{rate.Name}»")
                : roomHourlyOverride is { } own ? (own, RoomHourPriceSource) : (hourlyPrice, HourPriceSource);

            if (lines.Count > 0 && lines[^1].UnitPrice == price && lines[^1].Source == source)
            {
                var last = lines[^1];
                lines[^1] = last with { To = from.AddHours(1), Hours = last.Hours + 1, Amount = last.Amount + price };
            }
            else
            {
                lines.Add(new HourPrice(from, from.AddHours(1), 1, price, price, source));
            }
        }

        var accommodation = lines.Sum(l => l.Amount);
        var discountAmount = discount?.AmountFor(accommodation) ?? 0m;
        var charges = services
            .Where(s => s.Quantity > 0)
            .Select(s => new ServiceCharge(s.Service.Id, s.Service.Name, s.Quantity, s.Service.Price, false, Money.Kgs(s.Service.Price * s.Quantity).Amount))
            .ToList();
        var servicesTotal = charges.Sum(c => c.Total);

        return new PriceQuote([], accommodation, discount?.Id, discount?.Name, discountAmount, charges, servicesTotal,
            accommodation - discountAmount + servicesTotal, PriceQuote.HourlyKind, lines);
    }
}
