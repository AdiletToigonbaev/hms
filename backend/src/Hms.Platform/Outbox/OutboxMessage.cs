namespace Hms.Platform.Outbox;

public enum OutboxStatus
{
    Pending,
    Done,
    Failed,
}

/// <summary>Сообщение для надёжной фоновой доставки (email, WhatsApp, интеграционные события).</summary>
public sealed class OutboxMessage
{
    private OutboxMessage()
    {
        Type = string.Empty;
        Payload = string.Empty;
    }

    internal OutboxMessage(Guid id, string type, string payload, string? dedupKey, DateTimeOffset now)
    {
        Id = id;
        Type = type;
        Payload = payload;
        DedupKey = dedupKey;
        Status = OutboxStatus.Pending;
        CreatedAt = now;
        NextAttemptAt = now;
    }

    public Guid Id { get; private set; }

    /// <summary>Тип сообщения — по нему выбирается обработчик (например, <c>email.send</c>).</summary>
    public string Type { get; private set; }

    /// <summary>JSON-содержимое. Для чувствительных типов очищается после успешной доставки.</summary>
    public string Payload { get; private set; }

    /// <summary>Ключ дедупликации: повторная постановка того же события игнорируется.</summary>
    public string? DedupKey { get; private set; }

    public OutboxStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public string? LastError { get; private set; }

    internal void MarkDone(DateTimeOffset now, bool clearPayload)
    {
        Status = OutboxStatus.Done;
        ProcessedAt = now;
        LastError = null;
        Attempts++;
        if (clearPayload)
        {
            Payload = "{}";
        }
    }

    internal void MarkFailedAttempt(DateTimeOffset now, string error, bool permanent, int maxAttempts)
    {
        Attempts++;
        LastError = error.Length > 1000 ? error[..1000] : error;
        if (permanent || Attempts >= maxAttempts)
        {
            Status = OutboxStatus.Failed;
            ProcessedAt = now;
            return;
        }

        NextAttemptAt = now + Backoff(Attempts);
    }

    /// <summary>Экспоненциальная пауза: 10 с, 20 с, 40 с … не более 1 часа.</summary>
    internal static TimeSpan Backoff(int attempts) =>
        TimeSpan.FromSeconds(Math.Min(10 * Math.Pow(2, Math.Max(0, attempts - 1)), 3600));
}
