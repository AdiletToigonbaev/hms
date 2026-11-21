namespace Hms.Modules.Identity.Application;

// ---- Аутентификация ----

/// <param name="AcceptPersonalData">Согласие на обработку персональных данных (обязательно).</param>
/// <param name="AcceptWhatsApp">Согласие на сообщения в WhatsApp (необязательно).</param>
public sealed record RegisterRequest(
    string FullName,
    string Email,
    string? Phone,
    string Password,
    bool AcceptPersonalData,
    bool AcceptWhatsApp);

public sealed record RegisterResponse(Guid UserId, bool EmailConfirmationRequired);

/// <param name="Login">Email или номер телефона.</param>
public sealed record LoginRequest(string Login, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record LogoutRequest(string RefreshToken);

public sealed record ConfirmEmailRequest(Guid UserId, string Token);

public sealed record EmailRequest(string Email);

public sealed record ResetPasswordRequest(Guid UserId, string Token, string NewPassword);

public sealed record TokenResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    MeResponse User);

// ---- Профиль ----

public sealed record MeResponse(
    Guid Id,
    string FullName,
    string? Email,
    bool EmailConfirmed,
    string? Phone,
    string UserType,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    bool MustChangePassword);

public sealed record UpdateMeRequest(string FullName, string? Phone);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>Удаление аккаунта — подтверждение паролем.</summary>
public sealed record DeleteAccountRequest(string Password);

// ---- Персонал ----

public sealed record StaffDto(
    Guid Id,
    string FullName,
    string? Email,
    string? Phone,
    string Role,
    string RoleDisplayName,
    bool IsActive,
    bool MustChangePassword,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

/// <param name="Role">Administrator или Housekeeper.</param>
/// <param name="InitialPassword">Временный пароль: сотрудник сменит его при первом входе.</param>
public sealed record CreateStaffRequest(string FullName, string? Email, string? Phone, string Role, string InitialPassword);

public sealed record UpdateStaffRequest(string FullName, string? Email, string? Phone);

public sealed record StaffResetPasswordRequest(string NewPassword);

public sealed record StaffQuery(string? Search, string? Role, bool? IsActive, int? Page, int? PageSize);

// ---- Права ----

public sealed record PermissionDto(string Code, string Group, string Description, bool OwnerOnly);

public sealed record RoleDto(string Name, string DisplayName, IReadOnlyList<string> Permissions);

public sealed record PermissionOverrideDto(string Code, bool IsGranted, decimal? LimitAmount);

public sealed record StaffPermissionsDto(
    Guid UserId,
    string Role,
    IReadOnlyList<string> RolePermissions,
    IReadOnlyList<PermissionOverrideDto> Overrides,
    IReadOnlyList<string> Effective,
    IReadOnlyList<PermissionDto> Configurable);

public sealed record SetPermissionsRequest(IReadOnlyList<PermissionOverrideDto> Overrides);
