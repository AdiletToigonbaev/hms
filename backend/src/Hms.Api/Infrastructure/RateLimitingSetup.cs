using System.Threading.RateLimiting;
using Hms.Modules.Identity.Infrastructure;
using Hms.Platform.Http;
using Hms.Platform.Security;
using Microsoft.AspNetCore.HttpOverrides;

namespace Hms.Api.Infrastructure;

public sealed class RateLimitingOptions
{
    public const string Section = "RateLimiting";

    /// <summary>Вход, регистрация, восстановление пароля: запросов в минуту с одного IP.</summary>
    public int AuthStrictPerMinute { get; set; } = 10;

    /// <summary>Обновление токенов и выход: запросов в минуту с одного IP.</summary>
    public int AuthRefreshPerMinute { get; set; } = 60;

    /// <summary>Публичный подбор номеров и расчёт цены: запросов в минуту с одного IP.</summary>
    public int PublicSearchPerMinute { get; set; } = 60;

    /// <summary>Создание и продление броней гостем на сайте: запросов в минуту с одного аккаунта.</summary>
    public int GuestBookingPerMinute { get; set; } = 10;
}

internal static class RateLimitingSetup
{
    public static IServiceCollection AddHmsRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(RateLimitingOptions.Section).Get<RateLimitingOptions>() ?? new RateLimitingOptions();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(IdentityEndpoints.AuthStrictRateLimit, http => PerIp(http, options.AuthStrictPerMinute));
            limiter.AddPolicy(IdentityEndpoints.AuthRefreshRateLimit, http => PerIp(http, options.AuthRefreshPerMinute));
            limiter.AddPolicy(Hms.Modules.Bookings.Infrastructure.BookingsModule.PublicSearchRateLimit, http => PerIp(http, options.PublicSearchPerMinute));
            limiter.AddPolicy(Hms.Modules.Bookings.Infrastructure.BookingsModule.GuestBookingRateLimit, http => PerUser(http, options.GuestBookingPerMinute));
            limiter.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                await Results.Problem(
                        statusCode: StatusCodes.Status429TooManyRequests,
                        title: "Слишком много запросов. Попробуйте через минуту.",
                        extensions: new Dictionary<string, object?> { [ProblemFields.Code] = "rate_limited" })
                    .ExecuteAsync(context.HttpContext);
            };
        });
        return services;
    }

    /// <summary>
    /// Доверять X-Forwarded-For от BFF/Nginx (иначе все запросы выглядят пришедшими с IP контейнера web).
    /// Включать только когда API недоступен из интернета напрямую.
    /// </summary>
    public static IServiceCollection AddHmsForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.ForwardLimit = 2;
            if (configuration.GetValue<bool>("ForwardedHeaders:TrustAllProxies"))
            {
                o.KnownIPNetworks.Clear();
                o.KnownProxies.Clear();
            }
        });
        return services;
    }

    private static RateLimitPartition<string> PerIp(HttpContext http, int permitLimit) =>
        Fixed("ip:" + (http.Connection.RemoteIpAddress?.ToString() ?? "unknown"), permitLimit);

    /// <summary>По аккаунту: смена IP не обходит лимит. Без входа (эндпоинт всё равно ответит 401) — по IP.</summary>
    private static RateLimitPartition<string> PerUser(HttpContext http, int permitLimit) =>
        http.User.FindFirst(HmsClaimTypes.Subject)?.Value is { } userId
            ? Fixed("user:" + userId, permitLimit)
            : PerIp(http, permitLimit);

    private static RateLimitPartition<string> Fixed(string key, int permitLimit) =>
        RateLimitPartition.GetFixedWindowLimiter(
            key,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
}
