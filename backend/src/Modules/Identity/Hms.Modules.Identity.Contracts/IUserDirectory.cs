namespace Hms.Modules.Identity.Contracts;

/// <summary>Имена пользователей для отображения (история статусов, журналы) — без доступа к таблицам Identity.</summary>
public interface IUserDirectory
{
    Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IEnumerable<Guid> userIds, CancellationToken ct);

    /// <summary>Активные сотрудники с ролью (например, уборщики — для назначения задач): id → имя.</summary>
    Task<IReadOnlyDictionary<Guid, string>> ListActiveInRoleAsync(string role, CancellationToken ct);
}

/// <summary>Лимиты сумм у прав (например, утверждение возвратов в пределах лимита).</summary>
public interface IPermissionLimits
{
    /// <summary>
    /// Лимит суммы права у пользователя: число — не больше этой суммы; null — без лимита
    /// (владелец или право выдано без лимита). Наличие самого права проверяется отдельно.
    /// </summary>
    Task<decimal?> GetLimitAsync(Guid userId, string permission, CancellationToken ct);
}
