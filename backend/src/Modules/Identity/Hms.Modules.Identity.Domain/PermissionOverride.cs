using Hms.Modules.Identity.Contracts;

namespace Hms.Modules.Identity.Domain;

/// <summary>Индивидуальная настройка права сотрудника владельцем: выдать или отозвать относительно роли.</summary>
public sealed class PermissionOverride
{
    private PermissionOverride()
    {
        PermissionCode = string.Empty;
    }

    public PermissionOverride(Guid userId, string permissionCode, bool isGranted, decimal? limitAmount)
    {
        UserId = userId;
        PermissionCode = permissionCode;
        IsGranted = isGranted;
        LimitAmount = limitAmount;
    }

    public Guid UserId { get; private set; }

    public string PermissionCode { get; private set; }

    /// <summary>true — выдано сверх роли; false — отозвано у роли.</summary>
    public bool IsGranted { get; private set; }

    /// <summary>Лимит суммы (KGS) для финансовых разрешений, например утверждения возвратов.</summary>
    public decimal? LimitAmount { get; private set; }
}

/// <summary>Расчёт итоговых прав пользователя.</summary>
public static class EffectivePermissions
{
    /// <summary>
    /// Итоговые права = права ролей ∪ выданные индивидуально − отозванные индивидуально.
    /// У владельца — всегда все права, индивидуальные настройки к нему не применяются.
    /// </summary>
    public static IReadOnlySet<string> Compute(
        IEnumerable<string> roles,
        Func<string, IEnumerable<string>> rolePermissions,
        IEnumerable<PermissionOverride> overrides)
    {
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(rolePermissions);
        ArgumentNullException.ThrowIfNull(overrides);

        var roleList = roles.ToList();
        if (roleList.Contains(Roles.Owner))
        {
            return PermissionCatalog.All.Select(p => p.Code).ToHashSet(StringComparer.Ordinal);
        }

        var result = roleList.SelectMany(rolePermissions).ToHashSet(StringComparer.Ordinal);
        foreach (var o in overrides)
        {
            if (o.IsGranted)
            {
                result.Add(o.PermissionCode);
            }
            else
            {
                result.Remove(o.PermissionCode);
            }
        }

        // Права «только владельца» не могут попасть к другим ролям даже по ошибке в данных.
        result.RemoveWhere(code => PermissionCatalog.Find(code) is null or { OwnerOnly: true });
        return result;
    }
}
