using Hms.Modules.Identity.Contracts;
using Hms.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hms.Modules.Identity.Infrastructure.Services;

internal sealed class UserDirectory(DbContext db, Microsoft.AspNetCore.Identity.UserManager<HmsUser> users) : IUserDirectory
{
    public async Task<IReadOnlyDictionary<Guid, string>> ListActiveInRoleAsync(string role, CancellationToken ct) =>
        (await users.GetUsersInRoleAsync(role)).Where(u => u.IsActive).OrderBy(u => u.FullName).ToDictionary(u => u.Id, u => u.FullName);

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

/// <summary>
/// Лимит суммы права: личный лимит сотрудника, иначе — лимит его роли; null — без лимита (владелец — всегда без лимита).
/// </summary>
internal sealed class PermissionLimits(DbContext db) : Hms.Modules.Identity.Contracts.IPermissionLimits
{
    public async Task<decimal?> GetLimitAsync(Guid userId, string permission, CancellationToken ct)
    {
        var personal = await db.Set<Hms.Modules.Identity.Domain.PermissionOverride>().AsNoTracking()
            .Where(o => o.UserId == userId && o.PermissionCode == permission && o.IsGranted)
            .Select(o => o.LimitAmount)
            .FirstOrDefaultAsync(ct);
        if (personal is not null)
        {
            return personal;
        }

        return await (
            from ur in db.Set<Microsoft.AspNetCore.Identity.IdentityUserRole<Guid>>()
            join r in db.Set<HmsRole>() on ur.RoleId equals r.Id
            join rp in db.Set<RolePermissionGrant>() on r.Id equals rp.RoleId
            where ur.UserId == userId && rp.PermissionCode == permission && r.Name != Roles.Owner
            select rp.LimitAmount)
            .FirstOrDefaultAsync(ct);
    }
}
