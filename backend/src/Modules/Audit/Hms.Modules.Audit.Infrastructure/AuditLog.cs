namespace Hms.Modules.Audit.Infrastructure;

/// <summary>Запись журнала аудита. Только вставка: UPDATE/DELETE запрещены триггером в БД.</summary>
public sealed class AuditLog
{
    private AuditLog()
    {
        Action = string.Empty;
    }

    internal AuditLog(
        Guid id,
        DateTimeOffset at,
        Guid? userId,
        string action,
        string? entityType,
        string? entityId,
        string? before,
        string? after,
        string? ip,
        string? userAgent,
        string? correlationId)
    {
        Id = id;
        At = at;
        UserId = userId;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        Before = before;
        After = after;
        Ip = ip;
        UserAgent = userAgent;
        CorrelationId = correlationId;
    }

    public Guid Id { get; private set; }

    public DateTimeOffset At { get; private set; }

    public Guid? UserId { get; private set; }

    public string Action { get; private set; }

    public string? EntityType { get; private set; }

    public string? EntityId { get; private set; }

    public string? Before { get; private set; }

    public string? After { get; private set; }

    public string? Ip { get; private set; }

    public string? UserAgent { get; private set; }

    public string? CorrelationId { get; private set; }
}
