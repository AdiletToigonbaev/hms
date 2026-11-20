using FluentValidation;
using Hms.Modules.Audit.Contracts;
using Hms.Modules.Hotel.Contracts;
using Hms.Modules.Identity.Contracts;
using Hms.Modules.Pricing.Application;
using Hms.Modules.Pricing.Contracts;
using Hms.Modules.Pricing.Domain;
using Hms.Modules.Rooms.Contracts;
using Hms.Platform.Http;
using Hms.Platform.Persistence;
using Hms.Platform.Security;
using Hms.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hms.Modules.Pricing.Infrastructure;

internal sealed class PricingModelConfigurator : IModelConfigurator
{
    public const string Schema = "pricing";

    public void Configure(ModelBuilder b)
    {
        b.Entity<RoomRate>(e =>
        {
            e.ToTable("room_rates", Schema, t =>
            {
                t.HasCheckConstraint("ck_room_rates_period", "date_to >= date_from");
                t.HasCheckConstraint("ck_room_rates_price", "price > 0");
                t.HasCheckConstraint("ck_room_rates_days", "days_of_week BETWEEN 1 AND 127");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Price).HasPrecision(14, 2);
            e.Property(x => x.DaysOfWeek).HasConversion<int>();
            e.Property(x => x.Unit).HasConversion<string>().HasMaxLength(10).HasDefaultValue(RateUnit.Night).HasSentinel(RateUnit.Night);
            e.HasIndex(x => new { x.RoomTypeId, x.DateFrom, x.DateTo });
        });

        b.Entity<Discount>(e =>
        {
            e.ToTable("discounts", Schema, t => t.HasCheckConstraint("ck_discounts_value", "value > 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Code).HasMaxLength(30);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Value).HasPrecision(14, 2);
            e.ToTable(t => t.HasCheckConstraint("ck_discounts_min_hours", "min_hours BETWEEN 0 AND 24"));
            e.HasIndex(x => new { x.HotelId, x.Code }).IsUnique().HasFilter("code IS NOT NULL");
        });

        b.Entity<ExtraService>(e =>
        {
            e.ToTable("services", Schema, t => t.HasCheckConstraint("ck_services_price", "price > 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.Price).HasPrecision(14, 2);
            e.HasIndex(x => new { x.HotelId, x.IsActive });
        });
    }
}

/// <summary>Расчёт стоимости для модуля Bookings: данные из БД + чистая функция <see cref="PriceCalculation"/>.</summary>
internal sealed class PriceCalculator(DbContext db, IHotelContext hotel) : IPriceCalculator
{
    public async Task<Result<PriceQuote>> QuoteAsync(PriceRequest r, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var rates = await RatesAsync(hotelId, r.RoomTypeId, RateUnit.Night, r.Stay.Start, r.Stay.End.AddDays(-1), ct);
        var discount = await DiscountAsync(hotelId, r.PromoCode, d => d.CheckApplicable(r.Stay), ct);
        if (discount.IsFailure)
        {
            return discount.Error;
        }

        var services = await ServicesAsync(hotelId, r.Services, ct);
        return services.IsFailure
            ? services.Error
            : PriceCalculation.Calculate(r.Stay, r.BasePrice, r.RoomPriceOverride, rates, discount.Value, services.Value);
    }

    public async Task<Result<PriceQuote>> QuoteHourlyAsync(HourlyPriceRequest r, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var first = DateOnly.FromDateTime(r.StartLocal);
        var last = DateOnly.FromDateTime(r.StartLocal.AddHours(r.Hours).AddTicks(-1));
        var rates = await RatesAsync(hotelId, r.RoomTypeId, RateUnit.Hour, first, last, ct);
        var discount = await DiscountAsync(hotelId, r.PromoCode, d => d.CheckApplicableHourly(first, r.Hours), ct);
        if (discount.IsFailure)
        {
            return discount.Error;
        }

        var services = await ServicesAsync(hotelId, r.Services, ct);
        return services.IsFailure
            ? services.Error
            : PriceCalculation.CalculateHourly(r.StartLocal, r.Hours, r.HourlyPrice, r.RoomHourlyOverride, rates, discount.Value, services.Value);
    }

    private Task<List<RoomRate>> RatesAsync(Guid hotelId, Guid roomTypeId, RateUnit unit, DateOnly from, DateOnly to, CancellationToken ct) =>
        db.Set<RoomRate>().AsNoTracking()
            .Where(x => x.HotelId == hotelId && x.RoomTypeId == roomTypeId && x.Unit == unit && x.IsActive && x.DateFrom <= to && x.DateTo >= from)
            .ToListAsync(ct);

    /// <summary>Промокод: null — без скидки; ошибка — код не найден или не подходит.</summary>
    private async Task<Result<Discount?>> DiscountAsync(Guid hotelId, string? promoCode, Func<Discount, Error?> check, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(promoCode))
        {
            return Result.Success<Discount?>(null);
        }

        var code = promoCode.Trim().ToUpperInvariant();
        var discount = await db.Set<Discount>().AsNoTracking().FirstOrDefaultAsync(d => d.HotelId == hotelId && d.Code == code, ct);
        if (discount is null)
        {
            return PricingErrors.PromoNotFound;
        }

        return check(discount) is { } error ? error : Result.Success<Discount?>(discount);
    }

    private async Task<Result<IReadOnlyList<(ExtraService Service, int Quantity)>>> ServicesAsync(Guid hotelId, IReadOnlyList<ServiceLine> requested, CancellationToken ct)
    {
        var ids = requested.Select(s => s.ServiceId).Distinct().ToList();
        var catalog = await db.Set<ExtraService>().AsNoTracking()
            .Where(s => s.HotelId == hotelId && s.IsActive && ids.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);
        if (catalog.Count != ids.Count)
        {
            return PricingErrors.ServiceNotFound;
        }

        IReadOnlyList<(ExtraService Service, int Quantity)> lines = requested
            .GroupBy(s => s.ServiceId)
            .Select(g => (catalog[g.Key], g.Sum(x => x.Quantity)))
            .ToList();
        return Result.Success(lines);
    }
}

internal sealed class PricingAdminService(DbContext db, IHotelContext hotel, IRoomDirectory rooms, IAuditTrail audit, TimeProvider time) : IPricingAdminService
{
    public async Task<IReadOnlyList<RateDto>> ListRatesAsync(Guid? roomTypeId, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        return await db.Set<RoomRate>().AsNoTracking()
            .Where(r => r.HotelId == hotelId && (roomTypeId == null || r.RoomTypeId == roomTypeId))
            .OrderByDescending(r => r.IsActive).ThenBy(r => r.DateFrom).ThenByDescending(r => r.Priority)
            .Select(r => new RateDto(r.Id, r.RoomTypeId, r.Name, r.DateFrom, r.DateTo, (int)r.DaysOfWeek, r.Price, r.Priority, r.IsActive, r.Unit.ToString()))
            .ToListAsync(ct);
    }

    public async Task<Result<RateDto>> SaveRateAsync(Guid? id, SaveRateRequest r, CancellationToken ct)
    {
        if (!await rooms.RoomTypeExistsAsync(r.RoomTypeId, ct))
        {
            return PricingAdminErrors.RoomTypeNotFound;
        }

        var hotelId = await hotel.GetHotelIdAsync(ct);
        var now = time.GetUtcNow();
        RoomRate rate;
        if (id is { } existingId)
        {
            var found = await db.Set<RoomRate>().FirstOrDefaultAsync(x => x.Id == existingId && x.HotelId == hotelId, ct);
            if (found is null)
            {
                return PricingAdminErrors.RateNotFound;
            }

            rate = found;
            var before = ToDto(rate);
            rate.Update(r.Name, r.DateFrom, r.DateTo, (WeekDays)r.DaysOfWeek, r.Price, r.Priority, r.IsActive, now);
            rate.SetUnit(Enum.Parse<RateUnit>(r.Unit));
            audit.Record(new AuditEntry("rate.updated", "RoomRate", rate.Id.ToString(), before, ToDto(rate)));
        }
        else
        {
            rate = new RoomRate(hotelId, r.RoomTypeId, r.Name, r.DateFrom, r.DateTo, (WeekDays)r.DaysOfWeek, r.Price, r.Priority, now);
            if (!r.IsActive)
            {
                rate.Update(r.Name, r.DateFrom, r.DateTo, (WeekDays)r.DaysOfWeek, r.Price, r.Priority, false, now);
            }

            rate.SetUnit(Enum.Parse<RateUnit>(r.Unit));
            db.Set<RoomRate>().Add(rate);
            audit.Record(new AuditEntry("rate.created", "RoomRate", rate.Id.ToString(), After: ToDto(rate)));
        }

        await db.SaveChangesAsync(ct);
        return ToDto(rate);
    }

    public async Task<Result> DeleteRateAsync(Guid id, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var rate = await db.Set<RoomRate>().FirstOrDefaultAsync(x => x.Id == id && x.HotelId == hotelId, ct);
        if (rate is null)
        {
            return PricingAdminErrors.RateNotFound;
        }

        // Удаление безопасно: цены в бронях хранятся снимком и от тарифа не зависят.
        db.Set<RoomRate>().Remove(rate);
        audit.Record(new AuditEntry("rate.deleted", "RoomRate", id.ToString(), Before: ToDto(rate)));
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<IReadOnlyList<DiscountDto>> ListDiscountsAsync(CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        return (await db.Set<Discount>().AsNoTracking().Where(d => d.HotelId == hotelId)
                .OrderByDescending(d => d.IsActive).ThenBy(d => d.Name).ToListAsync(ct))
            .Select(ToDto).ToList();
    }

    public async Task<Result<DiscountDto>> SaveDiscountAsync(Guid? id, SaveDiscountRequest r, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var code = string.IsNullOrWhiteSpace(r.Code) ? null : r.Code.Trim().ToUpperInvariant();
        if (code is not null && await db.Set<Discount>().AnyAsync(d => d.HotelId == hotelId && d.Code == code && d.Id != id, ct))
        {
            return PricingAdminErrors.CodeTaken;
        }

        var now = time.GetUtcNow();
        Discount discount;
        if (id is { } existingId)
        {
            var found = await db.Set<Discount>().FirstOrDefaultAsync(d => d.Id == existingId && d.HotelId == hotelId, ct);
            if (found is null)
            {
                return PricingAdminErrors.DiscountNotFound;
            }

            discount = found;
        }
        else
        {
            discount = new Discount(hotelId, now);
            db.Set<Discount>().Add(discount);
        }

        var before = id is null ? null : ToDto(discount);
        discount.Update(r.Name, code, Enum.Parse<DiscountType>(r.Type), r.Value, r.ValidFrom, r.ValidTo, r.MinNights, now);
        discount.SetMinHours(r.MinHours);
        audit.Record(new AuditEntry(id is null ? "discount.created" : "discount.updated", "Discount", discount.Id.ToString(), before, ToDto(discount)));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return PricingAdminErrors.CodeTaken;
        }

        return ToDto(discount);
    }

    public async Task<Result<DiscountDto>> SetDiscountActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var discount = await db.Set<Discount>().FirstOrDefaultAsync(d => d.Id == id && d.HotelId == hotelId, ct);
        if (discount is null)
        {
            return PricingAdminErrors.DiscountNotFound;
        }

        discount.SetActive(active, time.GetUtcNow());
        audit.Record(new AuditEntry(active ? "discount.activated" : "discount.deactivated", "Discount", id.ToString()));
        await db.SaveChangesAsync(ct);
        return ToDto(discount);
    }

    public async Task<IReadOnlyList<ServiceDto>> ListServicesAsync(bool publicOnly, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        return await db.Set<ExtraService>().AsNoTracking()
            .Where(s => s.HotelId == hotelId && (!publicOnly || (s.IsActive && s.IsPublic)))
            .OrderByDescending(s => s.IsActive).ThenBy(s => s.Name)
            .Select(s => new ServiceDto(s.Id, s.Name, s.Description, s.Price, s.IsPerNight, s.IsPublic, s.IsActive))
            .ToListAsync(ct);
    }

    public async Task<Result<ServiceDto>> SaveServiceAsync(Guid? id, SaveServiceRequest r, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var now = time.GetUtcNow();
        ExtraService service;
        if (id is { } existingId)
        {
            var found = await db.Set<ExtraService>().FirstOrDefaultAsync(s => s.Id == existingId && s.HotelId == hotelId, ct);
            if (found is null)
            {
                return PricingAdminErrors.ServiceNotFound;
            }

            service = found;
        }
        else
        {
            service = new ExtraService(hotelId, now);
            db.Set<ExtraService>().Add(service);
        }

        service.Update(r.Name, r.Description, r.Price, r.IsPerNight, r.IsPublic, now);
        audit.Record(new AuditEntry(id is null ? "service.created" : "service.updated", "Service", service.Id.ToString(), After: new { service.Name, service.Price, service.IsPerNight }));
        await db.SaveChangesAsync(ct);
        return ToDto(service);
    }

    public async Task<Result<ServiceDto>> SetServiceActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var service = await db.Set<ExtraService>().FirstOrDefaultAsync(s => s.Id == id && s.HotelId == hotelId, ct);
        if (service is null)
        {
            return PricingAdminErrors.ServiceNotFound;
        }

        service.SetActive(active, time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return ToDto(service);
    }

    private static RateDto ToDto(RoomRate r) => new(r.Id, r.RoomTypeId, r.Name, r.DateFrom, r.DateTo, (int)r.DaysOfWeek, r.Price, r.Priority, r.IsActive, r.Unit.ToString());

    private static DiscountDto ToDto(Discount d) => new(d.Id, d.Name, d.Code, d.Type.ToString(), d.Value, d.ValidFrom, d.ValidTo, d.MinNights, d.IsActive, d.MinHours);

    private static ServiceDto ToDto(ExtraService s) => new(s.Id, s.Name, s.Description, s.Price, s.IsPerNight, s.IsPublic, s.IsActive);
}

public static class PricingModule
{
    public static IServiceCollection AddPricingModule(this IServiceCollection services)
    {
        services.AddSingleton<IModelConfigurator, PricingModelConfigurator>();
        services.AddScoped<IPriceCalculator, PriceCalculator>();
        services.AddScoped<IPricingAdminService, PricingAdminService>();
        services.AddValidatorsFromAssemblyContaining<SaveRateRequestValidator>(includeInternalTypes: true);
        return services;
    }

    public static IEndpointRouteBuilder MapPricingEndpoints(this IEndpointRouteBuilder api)
    {
        string[] read = [Permissions.RateView, Permissions.RateManage, Permissions.BookingCreate];

        var rates = api.MapGroup("/rates").WithTags("Pricing");
        rates.MapGet("/", async (Guid? roomTypeId, IPricingAdminService s, CancellationToken ct) => Results.Ok(await s.ListRatesAsync(roomTypeId, ct)))
            .RequireAnyPermission(read);
        rates.MapPost("/", async (SaveRateRequest r, IPricingAdminService s, CancellationToken ct) =>
                (await s.SaveRateAsync(null, r, ct)).ToHttpResult(x => Results.Created($"/api/v1/rates/{x.Id}", x)))
            .WithValidation<SaveRateRequest>().RequirePermission(Permissions.RateManage);
        rates.MapPut("/{id:guid}", async (Guid id, SaveRateRequest r, IPricingAdminService s, CancellationToken ct) =>
                (await s.SaveRateAsync(id, r, ct)).ToHttpResult())
            .WithValidation<SaveRateRequest>().RequirePermission(Permissions.RateManage);
        rates.MapDelete("/{id:guid}", async (Guid id, IPricingAdminService s, CancellationToken ct) => (await s.DeleteRateAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.RateManage);

        var discounts = api.MapGroup("/discounts").WithTags("Pricing");
        discounts.MapGet("/", async (IPricingAdminService s, CancellationToken ct) => Results.Ok(await s.ListDiscountsAsync(ct)))
            .RequireAnyPermission(read);
        discounts.MapPost("/", async (SaveDiscountRequest r, IPricingAdminService s, CancellationToken ct) =>
                (await s.SaveDiscountAsync(null, r, ct)).ToHttpResult(x => Results.Created($"/api/v1/discounts/{x.Id}", x)))
            .WithValidation<SaveDiscountRequest>().RequirePermission(Permissions.RateManage);
        discounts.MapPut("/{id:guid}", async (Guid id, SaveDiscountRequest r, IPricingAdminService s, CancellationToken ct) =>
                (await s.SaveDiscountAsync(id, r, ct)).ToHttpResult())
            .WithValidation<SaveDiscountRequest>().RequirePermission(Permissions.RateManage);
        discounts.MapPost("/{id:guid}/deactivate", async (Guid id, IPricingAdminService s, CancellationToken ct) =>
            (await s.SetDiscountActiveAsync(id, false, ct)).ToHttpResult()).RequirePermission(Permissions.RateManage);
        discounts.MapPost("/{id:guid}/activate", async (Guid id, IPricingAdminService s, CancellationToken ct) =>
            (await s.SetDiscountActiveAsync(id, true, ct)).ToHttpResult()).RequirePermission(Permissions.RateManage);

        var services = api.MapGroup("/services").WithTags("Pricing");
        services.MapGet("/", async (IPricingAdminService s, CancellationToken ct) => Results.Ok(await s.ListServicesAsync(false, ct)))
            .RequireAnyPermission(read);
        services.MapPost("/", async (SaveServiceRequest r, IPricingAdminService s, CancellationToken ct) =>
                (await s.SaveServiceAsync(null, r, ct)).ToHttpResult(x => Results.Created($"/api/v1/services/{x.Id}", x)))
            .WithValidation<SaveServiceRequest>().RequirePermission(Permissions.RateManage);
        services.MapPut("/{id:guid}", async (Guid id, SaveServiceRequest r, IPricingAdminService s, CancellationToken ct) =>
                (await s.SaveServiceAsync(id, r, ct)).ToHttpResult())
            .WithValidation<SaveServiceRequest>().RequirePermission(Permissions.RateManage);
        services.MapPost("/{id:guid}/deactivate", async (Guid id, IPricingAdminService s, CancellationToken ct) =>
            (await s.SetServiceActiveAsync(id, false, ct)).ToHttpResult()).RequirePermission(Permissions.RateManage);
        services.MapPost("/{id:guid}/activate", async (Guid id, IPricingAdminService s, CancellationToken ct) =>
            (await s.SetServiceActiveAsync(id, true, ct)).ToHttpResult()).RequirePermission(Permissions.RateManage);

        api.MapGet("/public/services", async (IPricingAdminService s, CancellationToken ct) => Results.Ok(await s.ListServicesAsync(true, ct)))
            .WithTags("Public").AllowAnonymous();
        return api;
    }
}
