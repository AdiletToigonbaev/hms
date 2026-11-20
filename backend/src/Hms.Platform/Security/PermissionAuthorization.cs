using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;

namespace Hms.Platform.Security;

/// <summary>Актуальное состояние доступа пользователя (кэшируется на короткое время).</summary>
public sealed record UserAccess(
    Guid UserId,
    bool IsActive,
    string SecurityStamp,
    bool MustChangePassword,
    IReadOnlySet<string> Roles,
    IReadOnlySet<string> Permissions);

/// <summary>
/// Источник прав пользователя (реализует модуль Identity). Права читаются из БД при каждой проверке
/// (с кэшем на секунды), поэтому отзыв права или блокировка действуют сразу, а не после истечения токена.
/// </summary>
public interface IUserAccessProvider
{
    Task<UserAccess?> GetAsync(Guid userId, CancellationToken cancellationToken);

    void Invalidate(Guid userId);
}

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

/// <summary>Политики вида <c>perm:booking.cancel</c> создаются на лету — не нужно регистрировать каждую.</summary>
internal sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public const string Prefix = "perm:";

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(policyName[Prefix.Length..]))
                .Build();
        }

        return await base.GetPolicyAsync(policyName);
    }
}

internal sealed class PermissionHandler(IUserAccessProvider access) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (!Guid.TryParse(context.User.FindFirst(HmsClaimTypes.Subject)?.Value, out var userId))
        {
            return;
        }

        var state = await access.GetAsync(userId, CancellationToken.None);
        if (state is { IsActive: true } && state.Permissions.Contains(requirement.Permission))
        {
            context.Succeed(requirement);
        }
    }
}

public static class PermissionEndpointExtensions
{
    /// <summary>Требует у пользователя разрешение (проверка на backend, ТЗ §15).</summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(PermissionPolicyProvider.Prefix + permission);
}
