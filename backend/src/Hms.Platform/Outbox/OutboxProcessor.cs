using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hms.Platform.Outbox;

public sealed class OutboxOptions
{
    public const string Section = "Outbox";

    /// <summary>Включена ли фоновая обработка (в тестах выключается и вызывается вручную).</summary>
    public bool Enabled { get; set; } = true;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    public int BatchSize { get; set; } = 20;

    public int MaxAttempts { get; set; } = 10;
}

/// <summary>Одна итерация обработки outbox. Безопасна при нескольких экземплярах (FOR UPDATE SKIP LOCKED).</summary>
public interface IOutboxProcessor
{
    /// <returns>Количество обработанных сообщений.</returns>
    Task<int> ProcessBatchAsync(CancellationToken cancellationToken);
}

internal sealed partial class OutboxProcessor(
    DbContext db,
    IEnumerable<IOutboxHandler> handlers,
    TimeProvider time,
    IOptions<OutboxOptions> options,
    ILogger<OutboxProcessor> logger) : IOutboxProcessor
{
    private readonly Dictionary<string, IOutboxHandler> _handlers = handlers.ToDictionary(h => h.Type, StringComparer.Ordinal);

    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var batch = await db.Set<OutboxMessage>()
            .FromSql($"""
                SELECT * FROM platform.outbox_messages
                WHERE status = 'Pending' AND next_attempt_at <= {now}
                ORDER BY created_at
                LIMIT {options.Value.BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        foreach (var message in batch)
        {
            if (!_handlers.TryGetValue(message.Type, out var handler))
            {
                message.MarkFailedAttempt(now, $"Нет обработчика для типа {message.Type}", permanent: true, options.Value.MaxAttempts);
                LogNoHandler(logger, message.Type, message.Id);
                continue;
            }

            try
            {
                await handler.HandleAsync(message.Payload, cancellationToken);
                message.MarkDone(time.GetUtcNow(), handler.IsSensitive);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var permanent = ex is PermanentOutboxException;
                message.MarkFailedAttempt(time.GetUtcNow(), ex.Message, permanent, options.Value.MaxAttempts);
                LogFailed(logger, ex, message.Type, message.Id, message.Attempts, permanent);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return batch.Count;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox: нет обработчика для {Type} (сообщение {MessageId})")]
    private static partial void LogNoHandler(ILogger logger, string type, Guid messageId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox: ошибка обработки {Type} (сообщение {MessageId}, попытка {Attempt}, постоянная: {Permanent})")]
    private static partial void LogFailed(ILogger logger, Exception exception, string type, Guid messageId, int attempt, bool permanent);
}

/// <summary>Фоновый цикл обработки outbox.</summary>
internal sealed partial class OutboxBackgroundService(
    IServiceScopeFactory scopes,
    IOptions<OutboxOptions> options,
    ILogger<OutboxBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.Value.PollInterval);
        do
        {
            try
            {
                int processed;
                do
                {
                    await using var scope = scopes.CreateAsyncScope();
                    processed = await scope.ServiceProvider.GetRequiredService<IOutboxProcessor>().ProcessBatchAsync(stoppingToken);
                }
                while (processed > 0 && !stoppingToken.IsCancellationRequested);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogLoopError(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox: ошибка цикла обработки")]
    private static partial void LogLoopError(ILogger logger, Exception exception);
}
