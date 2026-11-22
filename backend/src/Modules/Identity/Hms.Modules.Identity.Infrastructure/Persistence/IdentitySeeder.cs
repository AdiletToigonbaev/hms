using Hms.Modules.Identity.Contracts;
using Hms.Modules.Identity.Domain;
using Hms.Platform.Persistence;
using Hms.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hms.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// Идемпотентный сидер: справочник разрешений и права ролей приводятся к PermissionCatalog,
/// роли создаются при отсутствии, первый владелец — из Bootstrap:Owner (если владельца ещё нет).
/// Индивидуальные настройки прав сотрудников (user_permissions) не затрагиваются.
/// </summary>
internal sealed partial class IdentitySeeder(
    DbContext db,
    RoleManager<HmsRole> roles,
    UserManager<HmsUser> users,
    TimeProvider time,
    IOptions<BootstrapOwnerOptions> bootstrap,
    ILogger<IdentitySeeder> logger) : IDataSeeder
{
    public int Order => 10;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var added = await SyncPermissionsAsync(cancellationToken);
        await EnsureRolesAsync();
        await SyncRolePermissionsAsync(added, cancellationToken);
        await EnsureOwnerAsync();
    }

    /// <summary>Синхронизирует каталог прав; возвращает коды, которых в БД ещё не было (новые в этом релизе).</summary>
    private async Task<HashSet<string>> SyncPermissionsAsync(CancellationToken ct)
    {
        var added = new HashSet<string>(StringComparer.Ordinal);
        var existing = await db.Set<PermissionEntity>().ToDictionaryAsync(p => p.Code, StringComparer.Ordinal, ct);
        foreach (var def in PermissionCatalog.All)
        {
            if (existing.Remove(def.Code, out var entity))
            {
                entity.Group = def.Group;
                entity.Description = def.Description;
                entity.OwnerOnly = def.OwnerOnly;
            }
            else
            {
                db.Set<PermissionEntity>().Add(new PermissionEntity { Code = def.Code, Group = def.Group, Description = def.Description, OwnerOnly = def.OwnerOnly });
                added.Add(def.Code);
            }
        }

        // Разрешения, удалённые из каталога, удаляются (каскадно — из ролей и индивидуальных настроек).
        db.Set<PermissionEntity>().RemoveRange(existing.Values);
        await db.SaveChangesAsync(ct);
        return added;
    }

    private async Task EnsureRolesAsync()
    {
        foreach (var name in Roles.All)
        {
            if (!await roles.RoleExistsAsync(name))
            {
                var result = await roles.CreateAsync(new HmsRole(name));
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException($"Не удалось создать роль {name}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
                }
            }
        }
    }

    /// <summary>
    /// Права ролей. Владелец — всегда весь каталог. Права администратора и уборщика настраивает владелец, поэтому
    /// сидер их не перезаписывает: добавляет только новые права каталога по умолчанию (на чистой БД — все по умолчанию)
    /// и убирает недопустимые для роли.
    /// </summary>
    private async Task SyncRolePermissionsAsync(HashSet<string> added, CancellationToken ct)
    {
        var roleIds = await db.Set<HmsRole>().ToDictionaryAsync(r => r.Name!, r => r.Id, StringComparer.Ordinal, ct);
        var current = await db.Set<RolePermissionGrant>().ToListAsync(ct);
        foreach (var role in Roles.All)
        {
            var roleId = roleIds[role];
            var grants = current.Where(rp => rp.RoleId == roleId).ToList();
            var have = grants.Select(rp => rp.PermissionCode).ToHashSet(StringComparer.Ordinal);
            if (role == Roles.Owner)
            {
                var all = PermissionCatalog.DefaultsFor(role).ToHashSet(StringComparer.Ordinal);
                db.Set<RolePermissionGrant>().RemoveRange(grants.Where(rp => !all.Contains(rp.PermissionCode)));
                db.Set<RolePermissionGrant>().AddRange(all.Where(c => !have.Contains(c)).Select(c => new RolePermissionGrant { RoleId = roleId, PermissionCode = c }));
                continue;
            }

            db.Set<RolePermissionGrant>().RemoveRange(grants.Where(rp => !PermissionCatalog.IsAllowedFor(role, rp.PermissionCode)));
            db.Set<RolePermissionGrant>().AddRange(PermissionCatalog.DefaultsFor(role)
                .Where(c => added.Contains(c) && !have.Contains(c))
                .Select(c => new RolePermissionGrant { RoleId = roleId, PermissionCode = c }));
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureOwnerAsync()
    {
        if ((await users.GetUsersInRoleAsync(Roles.Owner)).Count > 0)
        {
            return;
        }

        var options = bootstrap.Value;
        if (string.IsNullOrWhiteSpace(options.Password) || (string.IsNullOrWhiteSpace(options.Email) && string.IsNullOrWhiteSpace(options.Phone)))
        {
            LogNoOwner(logger);
            return;
        }

        var owner = new HmsUser
        {
            Id = Guid.CreateVersion7(),
            FullName = string.IsNullOrWhiteSpace(options.FullName) ? "Владелец" : options.FullName.Trim(),
            Email = string.IsNullOrWhiteSpace(options.Email) ? null : options.Email.Trim().ToLowerInvariant(),
            PhoneNumber = string.IsNullOrWhiteSpace(options.Phone) ? null : PhoneNumber.Parse(options.Phone).Value.Value,
            UserType = UserType.Staff,
            // Пароль из переменных окружения — временный: владелец сменит его при первом входе.
            MustChangePassword = true,
            CreatedAt = time.GetUtcNow(),
        };
        owner.EmailConfirmed = owner.Email is not null;
        owner.UserName = owner.Id.ToString("N");

        var created = await users.CreateAsync(owner, options.Password);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException($"Не удалось создать владельца: {string.Join("; ", created.Errors.Select(e => e.Description))}");
        }

        await users.AddToRoleAsync(owner, Roles.Owner);
        LogOwnerCreated(logger, owner.Id);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Владелец не создан: задайте Bootstrap__Owner__Email (или Phone) и Bootstrap__Owner__Password и повторите migrate")]
    private static partial void LogNoOwner(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Создан первый владелец {UserId}")]
    private static partial void LogOwnerCreated(ILogger logger, Guid userId);
}
