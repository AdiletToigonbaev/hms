using Hms.Platform.Time;

namespace Microsoft.Extensions.DependencyInjection;

public static class PlatformServiceCollectionExtensions
{
    /// <summary>Регистрирует общие сервисы платформы (время и т. д.).</summary>
    public static IServiceCollection AddPlatform(this IServiceCollection services, string timeZoneId = HotelClock.DefaultTimeZoneId)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IHotelClock>(sp => new HotelClock(sp.GetRequiredService<TimeProvider>(), timeZoneId));
        return services;
    }
}
