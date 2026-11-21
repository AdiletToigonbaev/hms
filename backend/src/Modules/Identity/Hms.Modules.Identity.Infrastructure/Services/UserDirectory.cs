using Hms.Modules.Identity.Contracts;
using Hms.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hms.Modules.Identity.Infrastructure.Services;

internal sealed class UserDirectory(DbContext db) : IUserDirectory
{
    public async Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await db.Set<HmsUser>().AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
    }
}

/// <summary>Лимит суммы права — из индивидуальной настройки прав сотрудника (владелец — без лимитов).</summary>
internal sealed class PermissionLimits(DbContext db) : Hms.Modules.Identity.Contracts.IPermissionLimits
{
    public Task<decimal?> GetLimitAsync(Guid userId, string permission, CancellationToken ct) =>
        db.Set<Hms.Modules.Identity.Domain.PermissionOverride>().AsNoTracking()
            .Where(o => o.UserId == userId && o.PermissionCode == permission && o.IsGranted)
            .Select(o => o.LimitAmount)
            .FirstOrDefaultAsync(ct);
}
