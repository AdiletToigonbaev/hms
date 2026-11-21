using System.Security.Claims;
using Hms.Modules.Identity.Application;
using Hms.Modules.Identity.Contracts;
using Hms.Platform.Http;
using Hms.Platform.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Hms.Modules.Identity.Infrastructure;

/// <summary>Endpoint доступен пользователю, который обязан сменить временный пароль.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class AllowWhenPasswordChangeRequiredAttribute : Attribute;

public static class IdentityEndpoints
{
    /// <summary>Политики ограничения частоты (настраиваются в Api).</summary>
    public const string AuthStrictRateLimit = "auth-strict";
    public const string AuthRefreshRateLimit = "auth-refresh";

    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder api)
    {
        MapAuth(api.MapGroup("/auth").WithTags("Auth").AllowAnonymous().WithMetadata(new AllowWhenPasswordChangeRequiredAttribute()));
        MapMe(api.MapGroup("/me").WithTags("Account").RequireAuthorization());
        MapStaff(api.MapGroup("/staff").WithTags("Staff").RequirePermission(Permissions.StaffManage));

        var access = api.MapGroup(string.Empty).WithTags("Staff").RequirePermission(Permissions.StaffManage);
        access.MapGet("/roles", async (IRoleQueries q, CancellationToken ct) => Results.Ok(await q.ListRolesAsync(ct)));
        access.MapGet("/roles/{name}/permissions", async (string name, IRoleQueries q, CancellationToken ct) => (await q.GetRoleAsync(name, ct)).ToHttpResult());
        access.MapGet("/permissions", (IRoleQueries q) => Results.Ok(q.ListPermissions()));
        return api;
    }

    private static void MapAuth(RouteGroupBuilder auth)
    {
        auth.MapPost("/register", async (RegisterRequest request, IAuthService service, CancellationToken ct) =>
                (await service.RegisterAsync(request, ct)).ToHttpResult(r => Results.Created($"/api/v1/me", r)))
            .WithValidation<RegisterRequest>()
            .RequireRateLimiting(AuthStrictRateLimit);

        auth.MapPost("/login", async (LoginRequest request, IAuthService service, CancellationToken ct) =>
                (await service.LoginAsync(request, ct)).ToHttpResult())
            .WithValidation<LoginRequest>()
            .RequireRateLimiting(AuthStrictRateLimit);

        auth.MapPost("/refresh", async (RefreshRequest request, IAuthService service, CancellationToken ct) =>
                (await service.RefreshAsync(request, ct)).ToHttpResult())
            .WithValidation<RefreshRequest>()
            .RequireRateLimiting(AuthRefreshRateLimit);

        auth.MapPost("/logout", async (LogoutRequest request, IAuthService service, CancellationToken ct) =>
                (await service.LogoutAsync(request, ct)).ToHttpResult())
            .WithValidation<LogoutRequest>()
            .RequireRateLimiting(AuthRefreshRateLimit);

        auth.MapPost("/confirm-email", async (ConfirmEmailRequest request, IAuthService service, CancellationToken ct) =>
                (await service.ConfirmEmailAsync(request, ct)).ToHttpResult())
            .WithValidation<ConfirmEmailRequest>()
            .RequireRateLimiting(AuthStrictRateLimit);

        auth.MapPost("/resend-confirmation", async (EmailRequest request, IAuthService service, CancellationToken ct) =>
            {
                await service.ResendConfirmationAsync(request, ct);
                return Results.Accepted();
            })
            .WithValidation<EmailRequest>()
            .RequireRateLimiting(AuthStrictRateLimit);

        auth.MapPost("/forgot-password", async (EmailRequest request, IAuthService service, CancellationToken ct) =>
            {
                await service.ForgotPasswordAsync(request, ct);
                return Results.Accepted();
            })
            .WithValidation<EmailRequest>()
            .RequireRateLimiting(AuthStrictRateLimit);

        auth.MapPost("/reset-password", async (ResetPasswordRequest request, IAuthService service, CancellationToken ct) =>
                (await service.ResetPasswordAsync(request, ct)).ToHttpResult())
            .WithValidation<ResetPasswordRequest>()
            .RequireRateLimiting(AuthStrictRateLimit);
    }

    private static void MapMe(RouteGroupBuilder me)
    {
        me.MapGet("/", async (ClaimsPrincipal user, IAccountService service, CancellationToken ct) =>
                (await service.GetMeAsync(UserId(user), ct)).ToHttpResult())
            .WithMetadata(new AllowWhenPasswordChangeRequiredAttribute());

        me.MapPut("/", async (UpdateMeRequest request, ClaimsPrincipal user, IAccountService service, CancellationToken ct) =>
                (await service.UpdateMeAsync(UserId(user), request, ct)).ToHttpResult())
            .WithValidation<UpdateMeRequest>();

        me.MapDelete("/", async ([Microsoft.AspNetCore.Mvc.FromBody] DeleteAccountRequest request, ClaimsPrincipal user, IAccountService service, CancellationToken ct) =>
                (await service.DeleteMeAsync(UserId(user), request, ct)).ToHttpResult())
            .RequireRateLimiting(AuthStrictRateLimit);

        me.MapPost("/change-password", async (ChangePasswordRequest request, ClaimsPrincipal user, IAccountService service, CancellationToken ct) =>
                (await service.ChangePasswordAsync(UserId(user), request, ct)).ToHttpResult())
            .WithValidation<ChangePasswordRequest>()
            .WithMetadata(new AllowWhenPasswordChangeRequiredAttribute())
            .RequireRateLimiting(AuthStrictRateLimit);
    }

    private static void MapStaff(RouteGroupBuilder staff)
    {
        staff.MapGet("/", async ([AsParameters] StaffQuery query, IStaffService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(query, ct)));

        staff.MapPost("/", async (CreateStaffRequest request, IStaffService service, CancellationToken ct) =>
                (await service.CreateAsync(request, ct)).ToHttpResult(s => Results.Created($"/api/v1/staff/{s.Id}", s)))
            .WithValidation<CreateStaffRequest>();

        staff.MapGet("/{id:guid}", async (Guid id, IStaffService service, CancellationToken ct) =>
            (await service.GetAsync(id, ct)).ToHttpResult());

        staff.MapPut("/{id:guid}", async (Guid id, UpdateStaffRequest request, IStaffService service, CancellationToken ct) =>
                (await service.UpdateAsync(id, request, ct)).ToHttpResult())
            .WithValidation<UpdateStaffRequest>();

        staff.MapPost("/{id:guid}/deactivate", async (Guid id, IStaffService service, CancellationToken ct) =>
            (await service.SetActiveAsync(id, false, ct)).ToHttpResult());

        staff.MapPost("/{id:guid}/activate", async (Guid id, IStaffService service, CancellationToken ct) =>
            (await service.SetActiveAsync(id, true, ct)).ToHttpResult());

        staff.MapPost("/{id:guid}/reset-password", async (Guid id, StaffResetPasswordRequest request, IStaffService service, CancellationToken ct) =>
                (await service.ResetPasswordAsync(id, request, ct)).ToHttpResult())
            .WithValidation<StaffResetPasswordRequest>();

        staff.MapGet("/{id:guid}/permissions", async (Guid id, IStaffService service, CancellationToken ct) =>
            (await service.GetPermissionsAsync(id, ct)).ToHttpResult());

        staff.MapPut("/{id:guid}/permissions", async (Guid id, SetPermissionsRequest request, IStaffService service, CancellationToken ct) =>
                (await service.SetPermissionsAsync(id, request, ct)).ToHttpResult())
            .WithValidation<SetPermissionsRequest>();
    }

    private static Guid UserId(ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirst(HmsClaimTypes.Subject)?.Value ?? throw new InvalidOperationException("Нет claim sub в токене."));
}
