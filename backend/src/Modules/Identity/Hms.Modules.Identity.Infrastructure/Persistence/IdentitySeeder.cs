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
        await SyncPermissionsAsync(cancellationToken);
        await EnsureRolesAsync();
        await SyncRolePermissionsAsync(cancellationToken);
        await EnsureOwnerAsync();
    }

    private async Task SyncPermissionsAsync(CancellationToken ct)
    {
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
            }
        }

        // Разрешения, удалённые из каталога, удаляются (каскадно — из ролей и индивидуальных настроек).
        db.Set<PermissionEntity>().RemoveRange(existing.Values);
        await db.SaveChangesAsync(ct);
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

    private async Task SyncRolePermissionsAsync(CancellationToken ct)
    {
        var roleIds = await db.Set<HmsRole>().ToDictionaryAsync(r => r.Name!, r => r.Id, StringComparer.Ordinal, ct);
        var current = await db.Set<RolePermissionGrant>().ToListAsync(ct);

        var desired = Roles.All
            .SelectMany(role => PermissionCatalog.DefaultsFor(role).Select(code => (RoleId: roleIds[role], Code: code)))
            .ToHashSet();
        var actual = current.Select(rp => (rp.RoleId, Code: rp.PermissionCode)).ToHashSet();

        db.Set<RolePermissionGrant>().RemoveRange(current.Where(rp => !desired.Contains((rp.RoleId, rp.PermissionCode))));
        db.Set<RolePermissionGrant>().AddRange(desired.Where(d => !actual.Contains(d)).Select(d => new RolePermissionGrant { RoleId = d.RoleId, PermissionCode = d.Code }));
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
