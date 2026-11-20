namespace Hms.Modules.Identity.Contracts;

/// <summary>Имена пользователей для отображения (история статусов, журналы) — без доступа к таблицам Identity.</summary>
public interface IUserDirectory
{
    Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IEnumerable<Guid> userIds, CancellationToken ct);
}
