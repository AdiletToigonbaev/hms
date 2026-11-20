namespace Hms.Modules.Audit.Contracts;

/// <summary>Запись журнала аудита (ТЗ §15, security-architecture §8).</summary>
/// <param name="Action">Код действия, например <c>auth.login.succeeded</c>, <c>staff.permissions.changed</c>.</param>
/// <param name="EntityType">Тип сущности (<c>User</c>, <c>Booking</c> …), если действие относится к сущности.</param>
/// <param name="EntityId">Идентификатор сущности.</param>
/// <param name="Before">Состояние до изменения (сериализуется в JSON). Без паролей и токенов.</param>
/// <param name="After">Состояние после изменения или детали события.</param>
/// <param name="ActorUserId">Кто выполнил действие, если отличается от текущего пользователя (например, вход).</param>
public sealed record AuditEntry(
    string Action,
    string? EntityType = null,
    string? EntityId = null,
    object? Before = null,
    object? After = null,
    Guid? ActorUserId = null);

public interface IAuditTrail
{
    /// <summary>
    /// Добавляет запись в текущую единицу работы: она сохранится вместе с бизнес-изменением
    /// (тем же SaveChanges, в той же транзакции) или не сохранится вовсе.
    /// </summary>
    void Record(AuditEntry entry);

    /// <summary>Записывает и сразу сохраняет — для событий без бизнес-изменений (например, неудачный вход).</summary>
    Task RecordNowAsync(AuditEntry entry, CancellationToken cancellationToken);
}
