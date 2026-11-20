using Hms.SharedKernel;

namespace Hms.Modules.Identity.Application;

public interface IAuthService
{
    Task<Result<RegisterResponse>> RegisterAsync(RegisterRequest request, CancellationToken ct);

    Task<Result<TokenResponse>> LoginAsync(LoginRequest request, CancellationToken ct);

    Task<Result<TokenResponse>> RefreshAsync(RefreshRequest request, CancellationToken ct);

    Task<Result> LogoutAsync(LogoutRequest request, CancellationToken ct);

    Task<Result> ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken ct);

    /// <summary>Всегда успешно — не раскрывает, существует ли аккаунт.</summary>
    Task ResendConfirmationAsync(EmailRequest request, CancellationToken ct);

    /// <summary>Всегда успешно — не раскрывает, существует ли аккаунт.</summary>
    Task ForgotPasswordAsync(EmailRequest request, CancellationToken ct);

    Task<Result> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct);
}

public interface IAccountService
{
    Task<Result<MeResponse>> GetMeAsync(Guid userId, CancellationToken ct);

    Task<Result<MeResponse>> UpdateMeAsync(Guid userId, UpdateMeRequest request, CancellationToken ct);

    /// <summary>Меняет пароль и выдаёт новую пару токенов (старые сессии завершаются).</summary>
    Task<Result<TokenResponse>> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct);
}

public interface IStaffService
{
    Task<PagedResult<StaffDto>> ListAsync(StaffQuery query, CancellationToken ct);

    Task<Result<StaffDto>> GetAsync(Guid id, CancellationToken ct);

    Task<Result<StaffDto>> CreateAsync(CreateStaffRequest request, CancellationToken ct);

    Task<Result<StaffDto>> UpdateAsync(Guid id, UpdateStaffRequest request, CancellationToken ct);

    Task<Result<StaffDto>> SetActiveAsync(Guid id, bool isActive, CancellationToken ct);

    Task<Result> ResetPasswordAsync(Guid id, StaffResetPasswordRequest request, CancellationToken ct);

    Task<Result<StaffPermissionsDto>> GetPermissionsAsync(Guid id, CancellationToken ct);

    Task<Result<StaffPermissionsDto>> SetPermissionsAsync(Guid id, SetPermissionsRequest request, CancellationToken ct);
}

public interface IRoleQueries
{
    Task<IReadOnlyList<RoleDto>> ListRolesAsync(CancellationToken ct);

    Task<Result<RoleDto>> GetRoleAsync(string name, CancellationToken ct);

    IReadOnlyList<PermissionDto> ListPermissions();
}
