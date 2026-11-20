using Hms.Modules.Identity.Domain;
using Hms.Modules.Identity.Infrastructure.Persistence;
using Hms.Platform.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Hms.Modules.Identity.Infrastructure.Services;

/// <summary>
/// Текущее состояние доступа пользователя из БД с кэшем на 30 секунд.
/// Изменения прав/блокировка в этом экземпляре API сбрасывают кэш сразу (Invalidate).
/// </summary>
internal sealed class UserAccessProvider(DbContext db, IMemoryCache cache) : IUserAccessProvider
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    public async Task<UserAccess?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(Key(userId), out UserAccess? cached))
        {
            return cached;
        }

        var user = await db.Set<HmsUser>().AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.IsActive, u.SecurityStamp, u.MustChangePassword })
            .FirstOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            return null;
        }

        var roles = await (
            from ur in db.Set<IdentityUserRole<Guid>>()
            join r in db.Set<HmsRole>() on ur.RoleId equals r.Id
            where ur.UserId == userId
            select new { r.Id, r.Name })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var roleIds = roles.Select(r => r.Id).ToList();
        var rolePermissions = await db.Set<RolePermissionGrant>().AsNoTracking()
            .Where(rp => roleIds.Contains(rp.RoleId))
            .ToListAsync(cancellationToken);
        var overrides = await db.Set<PermissionOverride>().AsNoTracking()
            .Where(o => o.UserId == userId)
            .ToListAsync(cancellationToken);

        var roleNames = roles.Select(r => r.Name!).ToHashSet(StringComparer.Ordinal);
        var byRoleName = roles.ToDictionary(
            r => r.Name!,
            r => rolePermissions.Where(rp => rp.RoleId == r.Id).Select(rp => rp.PermissionCode).ToList(),
            StringComparer.Ordinal);

        var state = new UserAccess(
            user.Id,
            user.IsActive,
            user.SecurityStamp ?? string.Empty,
            user.MustChangePassword,
            roleNames,
            EffectivePermissions.Compute(roleNames, name => byRoleName.GetValueOrDefault(name, []), overrides));

        cache.Set(Key(userId), state, CacheDuration);
        return state;
    }

    public void Invalidate(Guid userId) => cache.Remove(Key(userId));

    private static string Key(Guid userId) => $"identity:access:{userId:N}";
}
