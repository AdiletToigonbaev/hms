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

        // Каждое сообщение — со своей точкой сохранения: если изменения обработчика не сохраняются (например, нарушено
        // ограничение БД), откатывается только это сообщение, а не вся пачка — одно «ядовитое» сообщение не блокирует очередь.
        // Блокировки строк пачки (FOR UPDATE) сохраняются до конца транзакции.
        foreach (var message in batch)
        {
            // После отката предыдущего сообщения контекст очищен — оставшиеся сообщения пачки снова отслеживаем.
            if (db.Entry(message).State == EntityState.Detached)
            {
                db.Attach(message);
            }

            if (!_handlers.TryGetValue(message.Type, out var handler))
            {
                message.MarkFailedAttempt(now, $"Нет обработчика для типа {message.Type}", permanent: true, options.Value.MaxAttempts);
                LogNoHandler(logger, message.Type, message.Id);
                await db.SaveChangesAsync(cancellationToken);
                continue;
            }

            var savepoint = $"outbox_{message.Id:N}";
            await transaction.CreateSavepointAsync(savepoint, cancellationToken);
            try
            {
                await handler.HandleAsync(message.Payload, cancellationToken);
                message.MarkDone(time.GetUtcNow(), handler.IsSensitive);
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not DbUpdateException)
            {
                // Ошибка обработчика (сеть, SMTP): его изменения (например, счётчик попыток сообщения) сохраняются вместе с отметкой.
                var permanent = ex is PermanentOutboxException;
                message.MarkFailedAttempt(time.GetUtcNow(), ex.Message, permanent, options.Value.MaxAttempts);
                LogFailed(logger, ex, message.Type, message.Id, message.Attempts, permanent);
                await SaveOrRollbackAsync(transaction, savepoint, message, ex, cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                await RollbackAndMarkAsync(transaction, savepoint, message.Id, ex, cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return batch.Count;
    }

    private async Task SaveOrRollbackAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, string savepoint, OutboxMessage message, Exception cause, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            await RollbackAndMarkAsync(transaction, savepoint, message.Id, new AggregateException(cause, ex), ct);
        }
    }

    /// <summary>Изменения обработчика не сохранились: откат к точке сохранения и отметка неудачной попытки отдельно.</summary>
    private async Task RollbackAndMarkAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, string savepoint, Guid messageId, Exception ex, CancellationToken ct)
    {
        await transaction.RollbackToSavepointAsync(savepoint, ct);
        db.ChangeTracker.Clear();
        var message = await db.Set<OutboxMessage>().FirstAsync(m => m.Id == messageId, ct);
        message.MarkFailedAttempt(time.GetUtcNow(), $"Не удалось сохранить результат: {ex.GetBaseException().Message}", permanent: false, options.Value.MaxAttempts);
        await db.SaveChangesAsync(ct);
        LogFailed(logger, ex, message.Type, message.Id, message.Attempts, false);
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
