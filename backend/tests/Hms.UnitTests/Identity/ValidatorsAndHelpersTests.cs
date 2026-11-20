using FluentValidation.TestHelper;
using Hms.Modules.Identity.Application;
using Hms.Modules.Identity.Contracts;
using Hms.Modules.Identity.Infrastructure.Services;
using Hms.Platform.Outbox;

namespace Hms.UnitTests.Identity;

public class ValidatorsAndHelpersTests
{
    [Fact]
    public void Register_Valid_Passes()
    {
        new RegisterRequestValidator()
            .TestValidate(new RegisterRequest("Асан Үсөнов", "asan@mail.kg", "0555123456", "long-enough-pass", true, false))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Register_WithoutPersonalDataConsent_Fails()
    {
        new RegisterRequestValidator()
            .TestValidate(new RegisterRequest("Асан", "asan@mail.kg", null, "long-enough-pass", false, true))
            .ShouldHaveValidationErrorFor(x => x.AcceptPersonalData);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("123456789")]
    public void Password_ShorterThan10_Fails(string password)
    {
        new RegisterRequestValidator()
            .TestValidate(new RegisterRequest("Асан", "asan@mail.kg", null, password, true, false))
            .ShouldHaveValidationErrorFor(x => x.Password)
            .WithErrorMessage("Пароль должен содержать не менее 10 символов.");
    }

    [Fact]
    public void CreateStaff_RequiresEmailOrPhone_AndManageableRole()
    {
        var validator = new CreateStaffRequestValidator();

        validator.TestValidate(new CreateStaffRequest("Уборщица Айжан", null, "0700111222", Roles.Housekeeper, "temp-password-1"))
            .ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new CreateStaffRequest("Без контактов", null, null, Roles.Housekeeper, "temp-password-1"))
            .IsValid.Should().BeFalse();
        validator.TestValidate(new CreateStaffRequest("Второй владелец", "o@x.kg", null, Roles.Owner, "temp-password-1"))
            .ShouldHaveValidationErrorFor(x => x.Role);
        validator.TestValidate(new CreateStaffRequest("Гость", "g@x.kg", null, Roles.Guest, "temp-password-1"))
            .ShouldHaveValidationErrorFor(x => x.Role);
    }

    [Fact]
    public void ChangePassword_SameAsCurrent_Fails()
    {
        new ChangePasswordRequestValidator()
            .TestValidate(new ChangePasswordRequest("same-password-1", "same-password-1"))
            .ShouldHaveValidationErrorFor(x => x.NewPassword);
    }

    [Fact]
    public void SetPermissions_DuplicateCodes_Fail()
    {
        new SetPermissionsRequestValidator()
            .TestValidate(new SetPermissionsRequest([
                new PermissionOverrideDto(Permissions.RefundApprove, true, 100m),
                new PermissionOverrideDto(Permissions.RefundApprove, false, null),
            ]))
            .IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("ivanov@mail.kg", "iv***@mail.kg")]
    [InlineData("a@b.kg", "a***@b.kg")]
    [InlineData("+996555123456", "+996***456")]
    [InlineData("12345", "***")]
    public void MaskLogin_HidesMostOfIdentifier(string login, string expected)
    {
        IdentityHelpers.MaskLogin(login).Should().Be(expected);
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    [InlineData(4, 80)]
    [InlineData(20, 3600)]
    public void OutboxBackoff_IsExponential_Capped(int attempts, int expectedSeconds)
    {
        OutboxMessage.Backoff(attempts).Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }
}
