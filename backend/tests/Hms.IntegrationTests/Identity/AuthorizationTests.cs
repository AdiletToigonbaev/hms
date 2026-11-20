using System.Net;
using System.Net.Http.Json;
using Hms.Modules.Identity.Application;
using Hms.Modules.Identity.Contracts;
using static Hms.IntegrationTests.TestApi;

namespace Hms.IntegrationTests.Identity;

public class AuthorizationTests(PostgresFixture db)
{
    private readonly TestApi _api = new(db);

    [Fact]
    public async Task Anonymous_ProtectedEndpoints_Return401()
    {
        var client = _api.Anonymous();
        foreach (var path in new[] { "/api/v1/me", "/api/v1/staff", "/api/v1/audit-logs", "/api/v1/roles" })
        {
            var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, path);
            (await ProblemCodeAsync(response)).Should().Be("auth.unauthorized");
        }
    }

    [Fact]
    public async Task ForgedToken_IsRejected()
    {
        var owner = await _api.LoginOwnerAsync();
        var parts = owner.AccessToken.Split('.');
        var tampered = $"{parts[0]}.{parts[1]}.{new string('A', parts[2].Length)}";

        (await _api.Authorized(tampered).GetAsync(new Uri("/api/v1/me", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(Roles.Administrator)]
    [InlineData(Roles.Housekeeper)]
    public async Task NonOwnerStaff_CannotManageStaffOrReadAudit(string role)
    {
        var (_, tokens) = await _api.CreateActiveStaffAsync(role);
        var client = _api.Authorized(tokens.AccessToken);

        foreach (var path in new[] { "/api/v1/staff", "/api/v1/audit-logs", "/api/v1/permissions" })
        {
            var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, path);
            (await ProblemCodeAsync(response)).Should().Be("auth.forbidden");
        }

        (await client.PostAsJsonAsync("/api/v1/staff", new CreateStaffRequest("Х", UniqueEmail(), null, Roles.Administrator, StaffPassword), Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Guest_CannotAccessStaffArea()
    {
        var (email, _) = await _api.RegisterConfirmedGuestAsync();
        var guest = await _api.LoginAsync(email, GuestPassword);

        (await _api.Authorized(guest.AccessToken).GetAsync(new Uri("/api/v1/staff", UriKind.Relative), Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task LoginForAllFourRoles_ReturnsRoleAndDefaultPermissions()
    {
        var owner = await _api.LoginOwnerAsync();
        var (_, admin) = await _api.CreateActiveStaffAsync(Roles.Administrator);
        var (_, housekeeper) = await _api.CreateActiveStaffAsync(Roles.Housekeeper);
        var (guestEmail, _) = await _api.RegisterConfirmedGuestAsync();
        var guest = await _api.LoginAsync(guestEmail, GuestPassword);

        owner.User.Roles.Should().Equal(Roles.Owner);
        owner.User.Permissions.Should().Contain([Permissions.StaffManage, Permissions.AuditView, Permissions.DashboardOwnerView]);

        admin.User.Roles.Should().Equal(Roles.Administrator);
        admin.User.Permissions.Should().Contain([Permissions.BookingCreate, Permissions.CheckInPerform, Permissions.PaymentRegister])
            .And.NotContain([Permissions.StaffManage, Permissions.FinanceReportView, Permissions.RefundApprove]);

        housekeeper.User.Roles.Should().Equal(Roles.Housekeeper);
        housekeeper.User.Permissions.Should().BeEquivalentTo([Permissions.HousekeepingWork, Permissions.MaintenanceCreate]);

        guest.User.Roles.Should().Equal(Roles.Guest);
        guest.User.Permissions.Should().BeEmpty();
    }

    [Fact]
    public async Task NewStaff_MustChangeTemporaryPassword_BeforeUsingOtherEndpoints()
    {
        var owner = await _api.LoginOwnerAsync();
        var email = UniqueEmail("temp");
        (await _api.Authorized(owner.AccessToken).PostAsJsonAsync("/api/v1/staff",
            new CreateStaffRequest("Временный Пароль", email, null, Roles.Administrator, StaffInitialPassword), Ct))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var first = await _api.LoginAsync(email, StaffInitialPassword);
        first.User.MustChangePassword.Should().BeTrue();
        var client = _api.Authorized(first.AccessToken);

        (await client.GetAsync(new Uri("/api/v1/me", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        var blocked = await client.PutAsJsonAsync("/api/v1/me", new UpdateMeRequest("Новое Имя", null), Ct);
        blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ProblemCodeAsync(blocked)).Should().Be("auth.password_change_required");

        var changed = await client.PostAsJsonAsync("/api/v1/me/change-password", new ChangePasswordRequest(StaffInitialPassword, StaffPassword), Ct);
        var tokens = (await changed.Content.ReadFromJsonAsync<TokenResponse>(Ct))!;
        tokens.User.MustChangePassword.Should().BeFalse();

        (await _api.Authorized(tokens.AccessToken).PutAsJsonAsync("/api/v1/me", new UpdateMeRequest("Новое Имя", null), Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync(new Uri("/api/v1/me", UriKind.Relative), Ct))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized, "токен до смены пароля больше не действует");
    }

    [Fact]
    public async Task DeactivatedStaff_LosesAccessImmediately()
    {
        var owner = await _api.LoginOwnerAsync();
        var (staff, tokens) = await _api.CreateActiveStaffAsync(Roles.Administrator);
        var adminClient = _api.Authorized(tokens.AccessToken);
        (await adminClient.GetAsync(new Uri("/api/v1/me", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await _api.Authorized(owner.AccessToken).PostAsync(new Uri($"/api/v1/staff/{staff.Id}/deactivate", UriKind.Relative), null, Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await adminClient.GetAsync(new Uri("/api/v1/me", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _api.Anonymous().PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(tokens.RefreshToken), Ct))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _api.LoginRawAsync(staff.Email!, StaffPassword)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await _api.Authorized(owner.AccessToken).PostAsync(new Uri($"/api/v1/staff/{staff.Id}/activate", UriKind.Relative), null, Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await _api.LoginAsync(staff.Email!, StaffPassword);
    }

    [Fact]
    public async Task OwnerOverrides_GrantAndRevoke_TakeEffectImmediately()
    {
        var owner = _api.Authorized((await _api.LoginOwnerAsync()).AccessToken);
        var (staff, tokens) = await _api.CreateActiveStaffAsync(Roles.Administrator);

        var set = await owner.PutAsJsonAsync($"/api/v1/staff/{staff.Id}/permissions", new SetPermissionsRequest(
        [
            new PermissionOverrideDto(Permissions.FinanceReportView, true, null),
            new PermissionOverrideDto(Permissions.RefundApprove, true, 5000m),
            new PermissionOverrideDto(Permissions.BookingCancel, false, null),
        ]), Ct);
        set.StatusCode.Should().Be(HttpStatusCode.OK, await set.Content.ReadAsStringAsync(Ct));
        var result = (await set.Content.ReadFromJsonAsync<StaffPermissionsDto>(Ct))!;
        result.Effective.Should().Contain([Permissions.FinanceReportView, Permissions.RefundApprove]).And.NotContain(Permissions.BookingCancel);
        result.Overrides.Should().ContainSingle(o => o.Code == Permissions.RefundApprove && o.LimitAmount == 5000m);

        // Тот же access-токен администратора — права уже новые
        var me = await _api.Authorized(tokens.AccessToken).GetFromJsonAsync<MeResponse>("/api/v1/me", Ct);
        me!.Permissions.Should().Contain(Permissions.FinanceReportView).And.NotContain(Permissions.BookingCancel);

        // Сброс настроек возвращает права роли
        await owner.PutAsJsonAsync($"/api/v1/staff/{staff.Id}/permissions", new SetPermissionsRequest([]), Ct);
        me = await _api.Authorized(tokens.AccessToken).GetFromJsonAsync<MeResponse>("/api/v1/me", Ct);
        me!.Permissions.Should().Contain(Permissions.BookingCancel).And.NotContain(Permissions.FinanceReportView);
    }

    [Theory]
    [InlineData(Permissions.StaffManage)]
    [InlineData(Permissions.AuditView)]
    [InlineData(Permissions.SettingsManage)]
    [InlineData("unknown.permission")]
    public async Task OwnerOnlyOrUnknownPermission_CannotBeGrantedToAdministrator(string code)
    {
        var owner = _api.Authorized((await _api.LoginOwnerAsync()).AccessToken);
        var (staff, _) = await _api.CreateActiveStaffAsync(Roles.Administrator);

        var response = await owner.PutAsJsonAsync($"/api/v1/staff/{staff.Id}/permissions",
            new SetPermissionsRequest([new PermissionOverrideDto(code, true, null)]), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ProblemCodeAsync(response)).Should().Be("staff.permission_not_configurable");
    }

    [Fact]
    public async Task OwnerAccount_CannotBeModifiedThroughStaffEndpoints()
    {
        var owner = await _api.LoginOwnerAsync();
        var client = _api.Authorized(owner.AccessToken);

        var deactivate = await client.PostAsync(new Uri($"/api/v1/staff/{owner.User.Id}/deactivate", UriKind.Relative), null, Ct);

        deactivate.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ProblemCodeAsync(deactivate)).Should().Be("staff.cannot_modify_owner");
    }

    [Fact]
    public async Task StaffEndpoints_DoNotExposeGuests()
    {
        var owner = _api.Authorized((await _api.LoginOwnerAsync()).AccessToken);
        var (_, guestId) = await _api.RegisterConfirmedGuestAsync();

        (await owner.GetAsync(new Uri($"/api/v1/staff/{guestId}", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.PostAsync(new Uri($"/api/v1/staff/{guestId}/deactivate", UriKind.Relative), null, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task StaffList_SearchAndFilter()
    {
        var owner = _api.Authorized((await _api.LoginOwnerAsync()).AccessToken);
        var (housekeeper, _) = await _api.CreateActiveStaffAsync(Roles.Housekeeper);

        var page = await owner.GetFromJsonAsync<Hms.SharedKernel.PagedResult<StaffDto>>(
            $"/api/v1/staff?search={Uri.EscapeDataString(housekeeper.Email!.ToUpperInvariant())}&role=Housekeeper", Ct);

        page!.Items.Should().ContainSingle(s => s.Id == housekeeper.Id);
        page.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task OwnerResetsStaffPassword_OldSessionsEnd_TemporaryPasswordRequired()
    {
        var owner = _api.Authorized((await _api.LoginOwnerAsync()).AccessToken);
        var (staff, tokens) = await _api.CreateActiveStaffAsync(Roles.Housekeeper);

        (await owner.PostAsJsonAsync($"/api/v1/staff/{staff.Id}/reset-password", new StaffResetPasswordRequest("brand-new-temp-pass"), Ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await _api.Anonymous().PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(tokens.RefreshToken), Ct))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _api.LoginAsync(staff.Email!, "brand-new-temp-pass")).User.MustChangePassword.Should().BeTrue();
    }
}
