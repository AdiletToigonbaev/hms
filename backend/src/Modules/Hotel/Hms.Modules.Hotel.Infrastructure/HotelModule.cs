using FluentValidation;
using Hms.Modules.Hotel.Application;
using Hms.Modules.Hotel.Contracts;
using Hms.Modules.Identity.Contracts;
using Hms.Platform.Http;
using Hms.Platform.Persistence;
using Hms.Platform.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Hms.Modules.Hotel.Infrastructure;

public static class HotelModule
{
    public static IServiceCollection AddHotelModule(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton<IModelConfigurator, HotelModelConfigurator>();
        services.AddScoped<IDataSeeder, HotelSeeder>();
        services.AddScoped<HotelContext>();
        services.AddScoped<IHotelContext>(sp => sp.GetRequiredService<HotelContext>());
        services.AddScoped<IHotelService, HotelService>();
        services.AddScoped<IIntegrationService, IntegrationService>();
        services.AddScoped<IHotelProfile, HotelProfileReader>();
        services.AddValidatorsFromAssemblyContaining<UpdateHotelRequestValidator>(includeInternalTypes: true);
        return services;
    }

    public static IEndpointRouteBuilder MapHotelEndpoints(this IEndpointRouteBuilder api)
    {
        // Просмотр — владелец и администратор (API spec §3), изменение — только владелец.
        var read = new[] { Permissions.SettingsManage, Permissions.DashboardOperationsView, Permissions.RoomManage };

        var hotel = api.MapGroup("/hotel").WithTags("Hotel");
        hotel.MapGet("/", async (IHotelService s, CancellationToken ct) => Results.Ok(await s.GetAsync(ct)))
            .RequireAnyPermission(read);
        hotel.MapPut("/", async (UpdateHotelRequest r, IHotelService s, CancellationToken ct) => (await s.UpdateAsync(r, ct)).ToHttpResult())
            .WithValidation<UpdateHotelRequest>()
            .RequirePermission(Permissions.SettingsManage);
        hotel.MapGet("/settings", async (IHotelService s, CancellationToken ct) => Results.Ok(await s.GetSettingsAsync(ct)))
            .RequireAnyPermission(read);
        hotel.MapPut("/settings", async (UpdateHotelSettingsRequest r, IHotelService s, CancellationToken ct) => (await s.UpdateSettingsAsync(r, ct)).ToHttpResult())
            .WithValidation<UpdateHotelSettingsRequest>()
            .RequirePermission(Permissions.SettingsManage);

        api.MapGet("/public/hotel-info", async (IHotelService s, CancellationToken ct) => Results.Ok(await s.GetPublicInfoAsync(ct)))
            .WithTags("Public")
            .AllowAnonymous();

        var integrations = api.MapGroup("/settings/integrations").WithTags("Integrations").RequirePermission(Permissions.IntegrationsManage);
        integrations.MapGet("/", async (IIntegrationService s, CancellationToken ct) => Results.Ok(await s.ListAsync(ct)));
        integrations.MapGet("/{provider}", async (string provider, IIntegrationService s, CancellationToken ct) => (await s.GetAsync(provider, ct)).ToHttpResult());
        integrations.MapPut("/{provider}", async (string provider, UpdateIntegrationRequest r, IIntegrationService s, CancellationToken ct) =>
                (await s.UpdateAsync(provider, r, ct)).ToHttpResult())
            .WithValidation<UpdateIntegrationRequest>();
        integrations.MapPost("/{provider}/test", async (string provider, IIntegrationService s, CancellationToken ct) =>
            (await s.TestAsync(provider, ct)).ToHttpResult());

        return api;
    }
}
