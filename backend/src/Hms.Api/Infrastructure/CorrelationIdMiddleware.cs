using System.Buffers;
using Serilog.Context;

namespace Hms.Api.Infrastructure;

/// <summary>
/// Принимает <c>X-Correlation-Id</c> от клиента (Next.js/Nginx) или генерирует новый,
/// возвращает его в ответе и добавляет в контекст логов.
/// Недопустимые значения (длина, символы) заменяются сгенерированным, чтобы исключить инъекции в логи.
/// </summary>
internal sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "CorrelationId";
    private const int MaxLength = 64;

    private static readonly SearchValues<char> AllowedChars =
        SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.:");

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].ToString();
        var correlationId = IsValid(incoming) ? incoming : Guid.NewGuid().ToString("N");

        context.Items[ItemKey] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty(ItemKey, correlationId))
        {
            await _next(context);
        }
    }

    public static string? Get(HttpContext context) => context.Items.TryGetValue(ItemKey, out var value) ? value as string : null;

    internal static bool IsValid(string? value) =>
        !string.IsNullOrEmpty(value)
        && value.Length <= MaxLength
        && !value.AsSpan().ContainsAnyExcept(AllowedChars);
}
