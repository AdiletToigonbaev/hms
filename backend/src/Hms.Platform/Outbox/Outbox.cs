using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Hms.Platform.Outbox;

/// <summary>
/// Постановка сообщения в outbox. Сообщение добавляется в текущий DbContext и сохраняется
/// вместе с бизнес-изменением вызывающего кода (одна транзакция) — отдельного SaveChanges здесь нет.
/// </summary>
public interface IOutbox
{
    void Enqueue<TPayload>(string type, TPayload payload, string? dedupKey = null);
}

internal sealed class Outbox(DbContext db, TimeProvider time) : IOutbox
{
    public void Enqueue<TPayload>(string type, TPayload payload, string? dedupKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        var json = JsonSerializer.Serialize(payload, JsonSerializerOptions.Web);
        db.Set<OutboxMessage>().Add(new OutboxMessage(Guid.CreateVersion7(), type, json, dedupKey, time.GetUtcNow()));
    }
}

/// <summary>Обработчик сообщений одного типа.</summary>
public interface IOutboxHandler
{
    string Type { get; }

    /// <summary>Очищать ли payload после успешной доставки (содержит токены, ПД).</summary>
    bool IsSensitive { get; }

    Task HandleAsync(string payload, CancellationToken cancellationToken);
}

/// <summary>Постоянная ошибка: повторять бессмысленно (например, некорректный адрес).</summary>
public sealed class PermanentOutboxException : Exception
{
    public PermanentOutboxException()
    {
    }

    public PermanentOutboxException(string message)
        : base(message)
    {
    }

    public PermanentOutboxException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Базовый типизированный обработчик.</summary>
public abstract class OutboxHandler<TPayload> : IOutboxHandler
{
    public abstract string Type { get; }

    public virtual bool IsSensitive => false;

    public Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var value = JsonSerializer.Deserialize<TPayload>(payload, JsonSerializerOptions.Web)
            ?? throw new PermanentOutboxException($"Пустой payload сообщения {Type}.");
        return HandleAsync(value, cancellationToken);
    }

    protected abstract Task HandleAsync(TPayload payload, CancellationToken cancellationToken);
}
