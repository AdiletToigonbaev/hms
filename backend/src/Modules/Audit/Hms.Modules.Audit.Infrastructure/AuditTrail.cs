using System.Text.Json;
using Hms.Modules.Audit.Contracts;
using Hms.Platform.Security;
using Microsoft.EntityFrameworkCore;

namespace Hms.Modules.Audit.Infrastructure;

internal sealed class AuditTrail(DbContext db, ICurrentUser currentUser, TimeProvider time) : IAuditTrail
{
    public void Record(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        db.Set<AuditLog>().Add(Create(entry));
    }

    public async Task RecordNowAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        // Отдельная вставка, не затрагивающая другие отслеживаемые изменения контекста.
        var log = Create(entry);
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO audit.audit_logs (id, at, user_id, action, entity_type, entity_id, before, after, ip, user_agent, correlation_id)
            VALUES ({log.Id}, {log.At}, {log.UserId}, {log.Action}, {log.EntityType}, {log.EntityId},
                    CAST({log.Before} AS jsonb), CAST({log.After} AS jsonb), {log.Ip}, {log.UserAgent}, {log.CorrelationId})
            """,
            cancellationToken);
    }

    private AuditLog Create(AuditEntry entry) => new(
        Guid.CreateVersion7(),
        time.GetUtcNow(),
        entry.ActorUserId ?? currentUser.UserId,
        entry.Action,
        entry.EntityType,
        entry.EntityId,
        Serialize(entry.Before),
        Serialize(entry.After),
        currentUser.IpAddress,
        currentUser.UserAgent,
        currentUser.CorrelationId);

    private static string? Serialize(object? value) =>
        value is null ? null : JsonSerializer.Serialize(value, JsonSerializerOptions.Web);
}
