using Hms.Modules.Identity.Application;
using Hms.Modules.Identity.Contracts;
using Hms.Modules.Identity.Domain;
using Hms.Modules.Identity.Infrastructure.Persistence;
using Hms.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Hms.Modules.Identity.Infrastructure.Services;

internal sealed class RoleQueries(DbContext db) : IRoleQueries
{
    public async Task<IReadOnlyList<RoleDto>> ListRolesAsync(CancellationToken ct)
    {
        var rows = await (
            from r in db.Set<HmsRole>()
            join rp in db.Set<RolePermissionGrant>() on r.Id equals rp.RoleId into perms
            from rp in perms.DefaultIfEmpty()
            select new { r.Name, Code = rp == null ? null : rp.PermissionCode })
            .AsNoTracking()
            .ToListAsync(ct);

        return Roles.All
            .Select(name => new RoleDto(
                name,
                Roles.DisplayName(name),
                rows.Where(x => x.Name == name && x.Code != null).Select(x => x.Code!).Order(StringComparer.Ordinal).ToList()))
            .ToList();
    }

    public async Task<Result<RoleDto>> GetRoleAsync(string name, CancellationToken ct)
    {
        var role = (await ListRolesAsync(ct)).FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        return role is null ? IdentityErrors.RoleNotFound : role;
    }

    public IReadOnlyList<PermissionDto> ListPermissions() =>
        PermissionCatalog.All.Select(p => new PermissionDto(p.Code, p.Group, p.Description, p.OwnerOnly)).ToList();
}
