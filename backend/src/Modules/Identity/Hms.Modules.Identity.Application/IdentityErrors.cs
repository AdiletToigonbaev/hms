using Hms.SharedKernel;

namespace Hms.Modules.Identity.Application;

/// <summary>Доменные ошибки модуля Identity (коды — в docs/api/api-specification.md).</summary>
public static class IdentityErrors
{
    // Единое сообщение для неверного логина/пароля и несуществующего пользователя — без раскрытия аккаунтов.
    public static readonly Error InvalidCredentials = Error.Unauthorized("auth.invalid_credentials", "Неверный логин или пароль.");
    public static readonly Error LockedOut = new("auth.locked_out", "Слишком много неудачных попыток. Повторите вход через 15 минут.", ErrorType.Forbidden);
    public static readonly Error EmailNotConfirmed = Error.Forbidden("auth.email_not_confirmed", "Подтвердите email по ссылке из письма.");
    public static readonly Error RefreshTokenInvalid = Error.Unauthorized("auth.refresh_token_invalid", "Сессия истекла. Войдите снова.");
    public static readonly Error RefreshTokenRotated = Error.Unauthorized("auth.refresh_token_rotated", "Сессия уже обновлена другим запросом.");
    public static readonly Error InvalidToken = Error.Validation("auth.invalid_token", "Ссылка недействительна или устарела.");
    public static readonly Error EmailTaken = Error.Conflict("auth.email_taken", "Пользователь с таким email уже зарегистрирован.");
    public static readonly Error PhoneTaken = Error.Conflict("auth.phone_taken", "Пользователь с таким телефоном уже зарегистрирован.");
    public static readonly Error WrongCurrentPassword = Error.Validation("auth.wrong_password", "Текущий пароль указан неверно.");
    public static readonly Error UserNotFound = Error.NotFound("user.not_found", "Пользователь не найден.");
    public static readonly Error StaffNotFound = Error.NotFound("staff.not_found", "Сотрудник не найден.");
    public static readonly Error CannotModifyOwner = Error.Forbidden("staff.cannot_modify_owner", "Учётную запись владельца нельзя изменить в разделе сотрудников.");
    public static readonly Error OverridesOnlyForAdministrator = Error.BusinessRule("staff.permissions_not_configurable", "Права этой роли не настраиваются.");
    public static readonly Error RoleNotConfigurable = Error.BusinessRule("role.not_configurable", "Права владельца не настраиваются: у него всегда все права.");
    public static readonly Error RoleNotFound = Error.NotFound("role.not_found", "Роль не найдена.");

    public static Error PermissionNotConfigurable(string code) =>
        Error.BusinessRule("staff.permission_not_configurable", $"Разрешение «{code}» нельзя выдать этой роли.");

    public static Error PasswordRejected(string description) =>
        Error.Validation("auth.password_rejected", description);
}
