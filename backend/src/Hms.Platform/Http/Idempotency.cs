using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hms.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hms.Platform.Http;

/// <summary>
/// Сохранённый результат идемпотентного запроса (architecture §6 п.5): повтор с тем же
/// <c>Idempotency-Key</c> возвращает тот же ответ, а не выполняет операцию повторно.
/// </summary>
public sealed class IdempotencyRecord
{
    private IdempotencyRecord()
    {
        Scope = string.Empty;
        Key = string.Empty;
        RequestHash = string.Empty;
    }

    internal IdempotencyRecord(string scope, string key, string requestHash, DateTimeOffset now, TimeSpan ttl)
    {
        Scope = scope;
        Key = key;
        RequestHash = requestHash;
        CreatedAt = now;
        ExpiresAt = now + ttl;
    }

    public string Scope { get; private set; }

    public string Key { get; private set; }

    public string RequestHash { get; private set; }

    /// <summary>null — запрос ещё выполняется.</summary>
    public int? ResponseStatus { get; private set; }

    public string? ResponseBody { get; private set; }

    public bool IsProblem { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    internal void Complete(int status, string? body, bool isProblem)
    {
        ResponseStatus = status;
        ResponseBody = body;
        IsProblem = isProblem;
    }
}

/// <summary>Фильтр endpoint'а: обязательный заголовок Idempotency-Key, повтор — из сохранённого ответа.</summary>
public sealed class IdempotencyFilter(string scope) : IEndpointFilter
{
    public const string Header = "Idempotency-Key";
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    public static readonly Error KeyRequired = Error.Validation("idempotency.key_required", "Нужен заголовок Idempotency-Key (уникальный ключ операции).");
    public static readonly Error KeyReused = Error.BusinessRule("idempotency.key_reused", "Этот ключ уже использован для другого запроса.");
    public static readonly Error InProgress = Error.Conflict("idempotency.in_progress", "Такой же запрос уже выполняется. Повторите через несколько секунд.");

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var http = context.HttpContext;
        var key = http.Request.Headers[Header].ToString();
        if (key.Length is < 8 or > 100 || !key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            return KeyRequired.ToProblem();
        }

        var db = http.RequestServices.GetRequiredService<DbContext>();
        var time = http.RequestServices.GetRequiredService<TimeProvider>();
        var userScope = $"{scope}:{http.User.FindFirst("sub")?.Value ?? "anonymous"}";
        var hash = Hash(context.Arguments);

        var set = db.Set<IdempotencyRecord>();
        var existing = await set.AsNoTracking().FirstOrDefaultAsync(r => r.Scope == userScope && r.Key == key, http.RequestAborted);
        if (existing is not null && existing.ExpiresAt > time.GetUtcNow())
        {
            return Replay(existing, hash);
        }

        // Захват ключа: вставка либо проходит, либо конфликтует с параллельным запросом с тем же ключом.
        var now = time.GetUtcNow();
        var inserted = await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO platform.idempotency_keys (scope, key, request_hash, created_at, expires_at, is_problem)
            VALUES ({userScope}, {key}, {hash}, {now}, {now + Ttl}, false)
            ON CONFLICT (scope, key) DO UPDATE SET request_hash = EXCLUDED.request_hash, created_at = EXCLUDED.created_at,
                expires_at = EXCLUDED.expires_at, response_status = NULL, response_body = NULL, is_problem = false
            WHERE platform.idempotency_keys.expires_at <= {now}
            """,
            http.RequestAborted);
        if (inserted == 0)
        {
            var current = await set.AsNoTracking().FirstAsync(r => r.Scope == userScope && r.Key == key, http.RequestAborted);
            return Replay(current, hash);
        }

        object? result;
        try
        {
            result = await next(context);
        }
        catch
        {
            await Release(db, userScope, key);
            throw;
        }

        var (status, body, isProblem) = Capture(result);
        if (status >= 500)
        {
            await Release(db, userScope, key);
            return result;
        }

        await db.Database.ExecuteSqlAsync(
            $"UPDATE platform.idempotency_keys SET response_status = {status}, response_body = CAST({body} AS jsonb), is_problem = {isProblem} WHERE scope = {userScope} AND key = {key}",
            CancellationToken.None);
        return result;
    }

    private static object Replay(IdempotencyRecord record, string hash)
    {
        if (!string.Equals(record.RequestHash, hash, StringComparison.Ordinal))
        {
            return KeyReused.ToProblem();
        }

        if (record.ResponseStatus is not { } status)
        {
            return InProgress.ToProblem();
        }

        return new StoredResult(status, record.ResponseBody, record.IsProblem);
    }

    private static async Task Release(DbContext db, string scope, string key) =>
        await db.Database.ExecuteSqlAsync($"DELETE FROM platform.idempotency_keys WHERE scope = {scope} AND key = {key}", CancellationToken.None);

    private static (int Status, string? Body, bool IsProblem) Capture(object? result) => result switch
    {
        ProblemHttpResult problem => (problem.StatusCode, JsonSerializer.Serialize(problem.ProblemDetails, JsonSerializerOptions.Web), true),
        IStatusCodeHttpResult { StatusCode: { } code } and IValueHttpResult value => (code, JsonSerializer.Serialize(value.Value, JsonSerializerOptions.Web), false),
        IStatusCodeHttpResult { StatusCode: { } code } => (code, null, false),
        _ => (StatusCodes.Status200OK, result is null ? null : JsonSerializer.Serialize(result, JsonSerializerOptions.Web), false),
    };

    private static string Hash(IList<object?> arguments)
    {
        // Хэш только тела запроса (DTO), без сервисов и токенов отмены.
        var payload = arguments.Where(a => a is not null && a.GetType().Namespace?.StartsWith("Hms.Modules", StringComparison.Ordinal) == true);
        var json = JsonSerializer.Serialize(payload, JsonSerializerOptions.Web);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    /// <summary>Повтор сохранённого ответа.</summary>
    private sealed class StoredResult(int status, string? body, bool isProblem) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = status;
            httpContext.Response.Headers["Idempotent-Replayed"] = "true";
            if (body is not null)
            {
                httpContext.Response.ContentType = isProblem ? "application/problem+json" : "application/json; charset=utf-8";
                await httpContext.Response.WriteAsync(body);
            }
        }
    }
}

public static class IdempotencyExtensions
{
    /// <summary>Обязательный Idempotency-Key для операции (платежи, брони, заселение/выселение).</summary>
    public static RouteHandlerBuilder WithIdempotency(this RouteHandlerBuilder builder, string scope) =>
        builder.AddEndpointFilter(new IdempotencyFilter(scope));
}
