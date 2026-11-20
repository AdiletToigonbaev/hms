using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;

namespace Hms.Api.Infrastructure;

/// <summary>Единый формат ошибок API — RFC 9457 (application/problem+json).</summary>
internal static class ProblemDetailsSetup
{
    public const string CodeKey = "code";
    public const string TraceIdKey = "traceId";
    public const string CorrelationIdKey = "correlationId";

    public static IServiceCollection AddHmsProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var http = context.HttpContext;
            var problem = context.ProblemDetails;

            problem.Instance ??= $"{http.Request.Method} {http.Request.Path}";
            problem.Extensions[TraceIdKey] = Activity.Current?.Id ?? http.TraceIdentifier;

            if (CorrelationIdMiddleware.Get(http) is { } correlationId)
            {
                problem.Extensions[CorrelationIdKey] = correlationId;
            }

            if (!problem.Extensions.ContainsKey(CodeKey))
            {
                problem.Extensions[CodeKey] = DefaultCode(problem.Status ?? http.Response.StatusCode);
            }

            // Не раскрываем детали исключений клиенту ни в одном окружении.
            problem.Extensions.Remove("exception");
            if (http.Features.Get<IExceptionHandlerFeature>() is not null)
            {
                problem.Detail = null;
            }
        });

        services.AddExceptionHandler<GlobalExceptionHandler>();
        return services;
    }

    private static string DefaultCode(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "request.invalid",
        StatusCodes.Status401Unauthorized => "auth.unauthorized",
        StatusCodes.Status403Forbidden => "auth.forbidden",
        StatusCodes.Status404NotFound => "not_found",
        StatusCodes.Status405MethodNotAllowed => "request.method_not_allowed",
        StatusCodes.Status409Conflict => "conflict",
        StatusCodes.Status415UnsupportedMediaType => "request.unsupported_media_type",
        StatusCodes.Status429TooManyRequests => "rate_limited",
        >= 500 => "internal_error",
        _ => "error",
    };
}
