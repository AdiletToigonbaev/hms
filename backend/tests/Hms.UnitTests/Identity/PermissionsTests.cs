using System.Reflection;
using Hms.Modules.Identity.Contracts;
using Hms.Modules.Identity.Domain;

namespace Hms.UnitTests.Identity;

public class PermissionsTests
{
    private static readonly string[] AllConstants = typeof(Permissions)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral)
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToArray();

    [Fact]
    public void Catalog_ContainsEveryPermissionConstant_ExactlyOnce()
    {
        PermissionCatalog.All.Select(p => p.Code).Should().BeEquivalentTo(AllConstants);
        PermissionCatalog.All.Select(p => p.Code).Should().OnlyHaveUniqueItems();
        PermissionCatalog.All.Should().OnlyContain(p => !string.IsNullOrWhiteSpace(p.Description) && !string.IsNullOrWhiteSpace(p.Group));
    }

    [Theory]
    [InlineData(Roles.Owner)]
    [InlineData(Roles.Administrator)]
    [InlineData(Roles.Housekeeper)]
    [InlineData(Roles.Guest)]
    public void RoleDefaults_ReferenceOnlyCatalogPermissions(string role)
    {
        PermissionCatalog.DefaultsFor(role).Should().OnlyContain(code => PermissionCatalog.Exists(code));
    }

    [Fact]
    public void OwnerDefaults_AreAllPermissions()
    {
        PermissionCatalog.DefaultsFor(Roles.Owner).Should().BeEquivalentTo(AllConstants);
    }

    [Fact]
    public void AdministratorDefaults_MatchSecurityMatrix()
    {
        var admin = PermissionCatalog.DefaultsFor(Roles.Administrator);

        admin.Should().Contain([Permissions.BookingCreate, Permissions.CheckInPerform, Permissions.CheckOutPerform,
            Permissions.PaymentRegister, Permissions.ExpenseCreate, Permissions.HousekeepingInspect, Permissions.WhatsAppSend]);
        // Матрица: ⚙ по умолчанию «нет» и «—»
        admin.Should().NotContain([Permissions.FinanceReportView, Permissions.ReportExport, Permissions.RefundApprove,
            Permissions.GuestAnonymize, Permissions.BookingCancelPenaltyOverride, Permissions.PaymentReverse]);
        admin.Should().OnlyContain(code => !PermissionCatalog.Find(code)!.OwnerOnly);
    }

    [Fact]
    public void Housekeeper_HasNoFinanceOrGuestAccess()
    {
        PermissionCatalog.DefaultsFor(Roles.Housekeeper).Should().BeEquivalentTo([Permissions.HousekeepingWork, Permissions.MaintenanceCreate]);
        PermissionCatalog.DefaultsFor(Roles.Guest).Should().BeEmpty();
    }

    [Fact]
    public void Effective_Owner_AlwaysHasEverything_IgnoringOverrides()
    {
        var owner = Guid.NewGuid();
        var result = EffectivePermissions.Compute(
            [Roles.Owner], _ => [], [new PermissionOverride(owner, Permissions.StaffManage, false, null)]);

        result.Should().BeEquivalentTo(AllConstants);
    }

    [Fact]
    public void Effective_AppliesGrantsAndDenials()
    {
        var user = Guid.NewGuid();
        var result = EffectivePermissions.Compute(
            [Roles.Administrator],
            PermissionCatalog.DefaultsFor,
            [
                new PermissionOverride(user, Permissions.FinanceReportView, true, null),
                new PermissionOverride(user, Permissions.BookingCancel, false, null),
            ]);

        result.Should().Contain(Permissions.FinanceReportView).And.NotContain(Permissions.BookingCancel).And.Contain(Permissions.BookingView);
    }

    [Fact]
    public void Effective_OwnerOnlyPermission_NeverLeaksToOtherRoles()
    {
        var user = Guid.NewGuid();
        var result = EffectivePermissions.Compute(
            [Roles.Administrator],
            _ => [Permissions.StaffManage, Permissions.BookingView],
            [new PermissionOverride(user, Permissions.AuditView, true, null), new PermissionOverride(user, "made.up", true, null)]);

        result.Should().BeEquivalentTo([Permissions.BookingView]);
    }

    [Theory]
    [InlineData(Permissions.FinanceReportView, true)]
    [InlineData(Permissions.RefundApprove, true)]
    [InlineData(Permissions.StaffManage, false)]
    [InlineData(Permissions.SettingsManage, false)]
    [InlineData("unknown", false)]
    public void ConfigurableForAdministrator(string code, bool expected)
    {
        PermissionCatalog.IsConfigurableForAdministrator(code).Should().Be(expected);
    }
}
