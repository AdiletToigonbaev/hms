using Hms.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Identity;

namespace Hms.Modules.Identity.Infrastructure.Persistence;

/// <summary>Пользователь (персонал или гость) на базе ASP.NET Core Identity.</summary>
public sealed class HmsUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = string.Empty;

    public UserType UserType { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Временный пароль: до смены доступны только профиль и смена пароля.</summary>
    public bool MustChangePassword { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    public DateTimeOffset? DeactivatedAt { get; set; }
}

public sealed class HmsRole : IdentityRole<Guid>
{
    public HmsRole()
    {
    }

    public HmsRole(string name)
        : base(name)
    {
        Id = Guid.CreateVersion7();
    }
}

/// <summary>Справочник разрешений (синхронизируется с PermissionCatalog сидером).</summary>
public sealed class PermissionEntity
{
    public string Code { get; set; } = string.Empty;

    public string Group { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool OwnerOnly { get; set; }
}

/// <summary>Права роли по умолчанию.</summary>
public sealed class RolePermissionGrant
{
    public Guid RoleId { get; set; }

    public string PermissionCode { get; set; } = string.Empty;
}
