using Hms.Modules.Audit.Contracts;
using Hms.Modules.Identity.Application;
using Hms.Modules.Identity.Contracts;
using Hms.Modules.Identity.Domain;
using Hms.Modules.Identity.Infrastructure.Persistence;
using Hms.Platform.Security;
using Hms.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Hms.Modules.Identity.Infrastructure.Services;

/// <summary>
/// Права ролей настраивает владелец. Изменение сразу действует на всех сотрудников роли (кэш прав сбрасывается),
/// кроме их личных отличий. Каждое изменение — в журнале действий.
/// </summary>
internal sealed class RolePermissionsService(DbContext db, IUserAccessProvider access, IAuditTrail audit) : IRolePermissionsService
{
    public async Task<Result<RolePermissionsDto>> GetAsync(string role, CancellationToken ct)
    {
        var name = Resolve(role);
        if (name is null)
        {
            return IdentityErrors.RoleNotFound;
        }

        return PermissionCatalog.ConfigurableRoles.Contains(name) ? await BuildAsync(name, ct) : IdentityErrors.RoleNotConfigurable;
    }

    public async Task<Result<RolePermissionsDto>> SetAsync(string role, SetRolePermissionsRequest request, CancellationToken ct)
    {
        var name = Resolve(role);
        if (name is null)
        {
            return IdentityErrors.RoleNotFound;
        }

        if (!PermissionCatalog.ConfigurableRoles.Contains(name))
        {
            return IdentityErrors.RoleNotConfigurable;
        }

        foreach (var g in request.Grants)
        {
            if (!PermissionCatalog.IsAllowedFor(name, g.Code))
            {
                return IdentityErrors.PermissionNotConfigurable(g.Code);
            }
        }

        return await ReplaceAsync(name, request.Grants, "role.permissions.changed", ct);
    }

    public async Task<Result<RolePermissionsDto>> ResetAsync(string role, CancellationToken ct)
    {
        var name = Resolve(role);
        if (name is null)
        {
            return IdentityErrors.RoleNotFound;
        }

        if (!PermissionCatalog.ConfigurableRoles.Contains(name))
        {
            return IdentityErrors.RoleNotConfigurable;
        }

        return await ReplaceAsync(name, PermissionCatalog.DefaultsFor(name).Select(c => new RoleGrantDto(c, null)).ToList(), "role.permissions.reset", ct);
    }

    private async Task<Result<RolePermissionsDto>> ReplaceAsync(string role, IReadOnlyList<RoleGrantDto> grants, string action, CancellationToken ct)
    {
        var roleId = await db.Set<HmsRole>().Where(r => r.Name == role).Select(r => r.Id).FirstAsync(ct);
        var existing = await db.Set<RolePermissionGrant>().Where(rp => rp.RoleId == roleId).ToListAsync(ct);
        var before = existing.OrderBy(x => x.PermissionCode, StringComparer.Ordinal).Select(x => new RoleGrantDto(x.PermissionCode, x.LimitAmount)).ToList();
        var after = grants
            .Select(g => new RoleGrantDto(g.Code, PermissionCatalog.LimitedPermissions.Contains(g.Code) ? g.LimitAmount : null))
            .OrderBy(x => x.Code, StringComparer.Ordinal)
            .ToList();

        db.Set<RolePermissionGrant>().RemoveRange(existing);
        db.Set<RolePermissionGrant>().AddRange(after.Select(g => new RolePermissionGrant { RoleId = roleId, PermissionCode = g.Code, LimitAmount = g.LimitAmount }));
        audit.Record(new AuditEntry(action, "Role", role, before, after));
        await db.SaveChangesAsync(ct);

        // Права сотрудников роли считаются заново сразу, а не через 30 секунд кэша.
        foreach (var userId in await UsersInRole(roleId).ToListAsync(ct))
        {
            access.Invalidate(userId);
        }

        return await BuildAsync(role, ct);
    }

    private async Task<RolePermissionsDto> BuildAsync(string role, CancellationToken ct)
    {
        var roleId = await db.Set<HmsRole>().Where(r => r.Name == role).Select(r => r.Id).FirstAsync(ct);
        var grants = await db.Set<RolePermissionGrant>().AsNoTracking().Where(rp => rp.RoleId == roleId)
            .Select(rp => new RoleGrantDto(rp.PermissionCode, rp.LimitAmount)).ToListAsync(ct);
        var configurable = PermissionCatalog.All.Where(p => PermissionCatalog.IsAllowedFor(role, p.Code))
            .Select(p => new PermissionDto(p.Code, p.Group, p.Description, p.OwnerOnly)).ToList();
        return new RolePermissionsDto(
            role,
            Roles.DisplayName(role),
            grants.OrderBy(g => g.Code, StringComparer.Ordinal).ToList(),
            PermissionCatalog.DefaultsFor(role).Order(StringComparer.Ordinal).ToList(),
            configurable,
            PermissionCatalog.LimitedPermissions.Where(c => PermissionCatalog.IsAllowedFor(role, c)).Order(StringComparer.Ordinal).ToList(),
            await UsersInRole(roleId).CountAsync(ct));
    }

    private IQueryable<Guid> UsersInRole(Guid roleId) =>
        db.Set<IdentityUserRole<Guid>>().Where(ur => ur.RoleId == roleId).Select(ur => ur.UserId);

    private static string? Resolve(string role) => Roles.All.FirstOrDefault(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
}
