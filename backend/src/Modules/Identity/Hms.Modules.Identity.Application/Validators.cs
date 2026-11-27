using FluentValidation;
using Hms.Modules.Identity.Contracts;
using Hms.SharedKernel;

namespace Hms.Modules.Identity.Application;

internal static class Rules
{
    public const int PasswordMinLength = 10;
    public const int PasswordMaxLength = 128;

    public static IRuleBuilderOptions<T, string> Password<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Введите пароль.")
            .MinimumLength(PasswordMinLength).WithMessage($"Пароль должен содержать не менее {PasswordMinLength} символов.")
            .MaximumLength(PasswordMaxLength).WithMessage($"Пароль не должен превышать {PasswordMaxLength} символов.");

    public static IRuleBuilderOptions<T, string> FullName<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Укажите ФИО.")
            .Must(v => v is not null && v.Trim().Length >= 2).WithMessage("ФИО слишком короткое.")
            .MaximumLength(200).WithMessage("ФИО не должно превышать 200 символов.");

    public static IRuleBuilderOptions<T, string?> Email<T>(this IRuleBuilder<T, string?> rule) =>
        rule.EmailAddress().WithMessage("Некорректный email.")
            .MaximumLength(256).WithMessage("Email не должен превышать 256 символов.");

    public static IRuleBuilderOptions<T, string?> Phone<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(v => PhoneNumber.Parse(v).IsSuccess).WithMessage("Некорректный номер телефона. Пример: +996 555 123 456.");
}

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.FullName).FullName();
        RuleFor(x => x.Email).NotEmpty().WithMessage("Укажите email.").Email();
        RuleFor(x => x.Phone).Phone().When(x => !string.IsNullOrWhiteSpace(x.Phone));
        RuleFor(x => x.Password).Password();
        RuleFor(x => x.AcceptPersonalData).Equal(true).WithMessage("Необходимо согласие на обработку персональных данных.");
        // Куда вернуть гостя после подтверждения email: только путь этого сайта (без открытого перенаправления).
        RuleFor(x => x.ReturnPath).Must(p => p is null || (p.Length <= 500 && p.StartsWith('/') && !p.StartsWith("//", StringComparison.Ordinal) && !p.StartsWith("/\\", StringComparison.Ordinal)))
            .WithMessage("Некорректный адрес возврата.");
        RuleFor(x => x.ConfirmBy).Must(c => c is null or "Email" or "WhatsApp").WithMessage("Подтверждение — по Email или WhatsApp.");
        RuleFor(x => x.Phone).NotEmpty().When(x => x.ConfirmBy == "WhatsApp").WithMessage("Для подтверждения в WhatsApp укажите телефон.");
        RuleFor(x => x.AcceptWhatsApp).Equal(true).When(x => x.ConfirmBy == "WhatsApp").WithMessage("Для подтверждения в WhatsApp нужно согласие на сообщения в WhatsApp.");
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Login).NotEmpty().WithMessage("Введите email или телефон.").MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().WithMessage("Введите пароль.").MaximumLength(Rules.PasswordMaxLength);
    }
}

public sealed class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator() => RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(200);
}

public sealed class LogoutRequestValidator : AbstractValidator<LogoutRequest>
{
    public LogoutRequestValidator() => RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(200);
}

public sealed class ConfirmEmailRequestValidator : AbstractValidator<ConfirmEmailRequest>
{
    public ConfirmEmailRequestValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Token).NotEmpty().MaximumLength(2000);
    }
}

public sealed class EmailRequestValidator : AbstractValidator<EmailRequest>
{
    public EmailRequestValidator() => RuleFor(x => x.Email).NotEmpty().WithMessage("Укажите email.").Email();
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Token).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.NewPassword).Password();
    }
}

public sealed class UpdateMeRequestValidator : AbstractValidator<UpdateMeRequest>
{
    public UpdateMeRequestValidator()
    {
        RuleFor(x => x.FullName).FullName();
        RuleFor(x => x.Phone).Phone().When(x => !string.IsNullOrWhiteSpace(x.Phone));
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage("Введите текущий пароль.");
        RuleFor(x => x.NewPassword).Password()
            .NotEqual(x => x.CurrentPassword).WithMessage("Новый пароль должен отличаться от текущего.");
    }
}

public sealed class CreateStaffRequestValidator : AbstractValidator<CreateStaffRequest>
{
    public CreateStaffRequestValidator()
    {
        RuleFor(x => x.FullName).FullName();
        RuleFor(x => x.Email).Email().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Phone).Phone().When(x => !string.IsNullOrWhiteSpace(x.Phone));
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Email) || !string.IsNullOrWhiteSpace(x.Phone))
            .WithName("email").WithMessage("Укажите email или телефон — по нему сотрудник будет входить.");
        RuleFor(x => x.Role)
            .Must(r => Roles.ManageableStaff.Contains(r)).WithMessage("Роль: Administrator или Housekeeper.");
        RuleFor(x => x.InitialPassword).Password();
    }
}

public sealed class UpdateStaffRequestValidator : AbstractValidator<UpdateStaffRequest>
{
    public UpdateStaffRequestValidator()
    {
        RuleFor(x => x.FullName).FullName();
        RuleFor(x => x.Email).Email().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Phone).Phone().When(x => !string.IsNullOrWhiteSpace(x.Phone));
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Email) || !string.IsNullOrWhiteSpace(x.Phone))
            .WithName("email").WithMessage("Укажите email или телефон — по нему сотрудник будет входить.");
    }
}

public sealed class StaffResetPasswordRequestValidator : AbstractValidator<StaffResetPasswordRequest>
{
    public StaffResetPasswordRequestValidator() => RuleFor(x => x.NewPassword).Password();
}

public sealed class SetRolePermissionsRequestValidator : AbstractValidator<SetRolePermissionsRequest>
{
    public SetRolePermissionsRequestValidator()
    {
        RuleFor(x => x.Grants).NotNull();
        RuleFor(x => x.Grants)
            .Must(list => list.Select(o => o.Code).Distinct(StringComparer.Ordinal).Count() == list.Count)
            .When(x => x.Grants is not null)
            .WithMessage("Каждое разрешение можно указать только один раз.");
        RuleForEach(x => x.Grants).ChildRules(o =>
        {
            o.RuleFor(x => x.Code).NotEmpty();
            o.RuleFor(x => x.LimitAmount).GreaterThan(0).When(x => x.LimitAmount is not null)
                .WithMessage("Лимит должен быть больше нуля.");
        });
    }
}

public sealed class SetPermissionsRequestValidator : AbstractValidator<SetPermissionsRequest>
{
    public SetPermissionsRequestValidator()
    {
        RuleFor(x => x.Overrides).NotNull();
        RuleFor(x => x.Overrides)
            .Must(list => list.Select(o => o.Code).Distinct(StringComparer.Ordinal).Count() == list.Count)
            .When(x => x.Overrides is not null)
            .WithMessage("Каждое разрешение можно указать только один раз.");
        RuleForEach(x => x.Overrides).ChildRules(o =>
        {
            o.RuleFor(x => x.Code).NotEmpty();
            o.RuleFor(x => x.LimitAmount).GreaterThan(0).When(x => x.LimitAmount is not null)
                .WithMessage("Лимит должен быть больше нуля.");
        });
    }
}

public sealed class ConfirmPhoneRequestValidator : AbstractValidator<ConfirmPhoneRequest>
{
    public ConfirmPhoneRequestValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().WithMessage("Введите код.").Matches("^[0-9]{6}$").WithMessage("Код — 6 цифр.");
    }
}

public sealed class ResendCodeRequestValidator : AbstractValidator<ResendCodeRequest>
{
    public ResendCodeRequestValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}
