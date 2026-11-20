using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Hms.Api.Infrastructure;

/// <summary>
/// /health/live — процесс жив (без внешних зависимостей);
/// /health/ready — готов обслуживать запросы (доступна БД).
/// Наружу через Nginx не публикуются.
/// </summary>
internal static class HealthEndpoints
{
    public const string ReadyTag = "ready";

    public static IServiceCollection AddHmsHealthChecks(this IServiceCollection services, string connectionString)
    {
        services.AddHealthChecks()
            .AddNpgSql(connectionString, name: "postgresql", tags: [ReadyTag], timeout: TimeSpan.FromSeconds(5));
        return services;
    }

    public static IEndpointRouteBuilder MapHmsHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteAsync,
        });
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag),
            ResponseWriter = WriteAsync,
        });
        return endpoints;
    }

    private static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        var payload = new
        {
            status = report.Status.ToString(),
            durationMs = (int)report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString() }),
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonSerializerOptions.Web));
    }
}
