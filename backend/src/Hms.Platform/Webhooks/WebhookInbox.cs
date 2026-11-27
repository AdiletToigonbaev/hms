using System.Security.Cryptography;
using System.Text;
using Hms.Platform.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Hms.Platform.Webhooks;

/// <summary>
/// Сырое событие вебхука (банки, WhatsApp). Хранится только событие с верной подписью; повтор того же тела
/// (провайдеры повторяют доставку часами и днями) отсекается уникальным ключом (Source, DedupKey).
/// </summary>
public sealed class WebhookEvent
{
    private WebhookEvent()
    {
        Source = DedupKey = Payload = string.Empty;
    }

    internal WebhookEvent(string source, string dedupKey, string payload, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        Source = source;
        DedupKey = dedupKey;
        Payload = payload;
        ReceivedAt = now;
    }

    public Guid Id { get; private set; }

    /// <summary>Источник: <c>whatsapp</c>, позже <c>mbank</c>, <c>optima</c>.</summary>
    public string Source { get; private set; }

    /// <summary>SHA-256 тела запроса.</summary>
    public string DedupKey { get; private set; }

    public string Payload { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public string? Error { get; private set; }

    public void MarkProcessed(DateTimeOffset now, string? error = null)
    {
        ProcessedAt = now;
        Error = error is { Length: > 1000 } ? error[..1000] : error;
    }
}

/// <summary>Приём вебхуков: событие сохраняется и ставится на обработку в outbox (тип <c>webhook.{source}</c>) одной транзакцией.</summary>
public interface IWebhookInbox
{
    /// <returns>False — такое событие уже принято (повтор доставки).</returns>
    Task<bool> AcceptAsync(string source, string payload, CancellationToken ct);
}

/// <summary>Payload outbox-сообщения обработки вебхука: в outbox только ссылка на событие.</summary>
public sealed record WebhookEventRef(Guid EventId);

internal sealed class WebhookInbox(DbContext db, IOutbox outbox, TimeProvider time) : IWebhookInbox
{
    private const string UniqueViolation = "23505";

    public async Task<bool> AcceptAsync(string source, string payload, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(payload);
        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        if (await db.Set<WebhookEvent>().AnyAsync(e => e.Source == source && e.DedupKey == key, ct))
        {
            return false;
        }

        var evt = new WebhookEvent(source, key, payload, time.GetUtcNow());
        db.Set<WebhookEvent>().Add(evt);
        outbox.Enqueue($"webhook.{source}", new WebhookEventRef(evt.Id));
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when ((ex.InnerException as System.Data.Common.DbException)?.SqlState == UniqueViolation)
        {
            // Тот же запрос пришёл параллельно — первый уже принят.
            db.ChangeTracker.Clear();
            return false;
        }
    }
}
