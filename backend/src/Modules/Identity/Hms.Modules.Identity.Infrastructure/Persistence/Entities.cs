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

/// <summary>Право роли (настраивает владелец).</summary>
public sealed class RolePermissionGrant
{
    public Guid RoleId { get; set; }

    public string PermissionCode { get; set; } = string.Empty;

    /// <summary>Лимит суммы для прав с лимитом (null — без лимита). Личный лимит сотрудника важнее.</summary>
    public decimal? LimitAmount { get; set; }
}

/// <summary>
/// Код подтверждения телефона в WhatsApp (регистрация — альтернатива письму, решение 2026-11-24).
/// Хранится только хэш; живёт 10 минут, не больше 5 попыток ввода.
/// </summary>
public sealed class PhoneVerificationCode
{
    public const int MaxAttempts = 5;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan ResendInterval = TimeSpan.FromSeconds(60);

    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Phone { get; set; } = string.Empty;

    public string CodeHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset? ConsumedAt { get; set; }

    public static string Hash(Guid userId, string code) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{userId:N}:{code}")));
}
