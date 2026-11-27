using System.Net;
using System.Net.Http.Json;
using Hms.IntegrationTests.Hotel;
using Hms.Modules.Finance.Application;
using Hms.Modules.Identity.Application;
using Hms.Modules.Identity.Contracts;
using Hms.Platform.Persistence;
using static Hms.IntegrationTests.TestApi;

namespace Hms.IntegrationTests.Identity;

/// <summary>
/// Права ролей настраивает владелец. Права роли общие для всех тестов — класс в коллекции без параллельного запуска,
/// каждый тест возвращает стандартные права роли.
/// </summary>
[Collection(nameof(GlobalSettingsGroup))]
public class RolePermissionsTests(PostgresFixture db)
{
    private readonly TestApi _api = new(db);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpClient> OwnerAsync() => _api.Authorized((await _api.LoginOwnerAsync()).AccessToken);

    private static async Task ResetAsync(HttpClient owner, string role) =>
        (await owner.PostAsync(new Uri($"/api/v1/roles/{role}/permissions/reset", UriKind.Relative), null, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

    [Fact]
    public async Task RolePermissions_ApplyToAllStaffOfRole_PersonalLimitWins()
    {
        var owner = await OwnerAsync();
        var (first, firstTokens) = await _api.CreateActiveStaffAsync(Roles.Administrator);
        var (_, secondTokens) = await _api.CreateActiveStaffAsync(Roles.Administrator);
        try
        {
            var settings = (await owner.GetFromJsonAsync<RolePermissionsDto>("/api/v1/roles/Administrator/settings", Ct))!;
            settings.Configurable.Should().NotContain(p => p.OwnerOnly);
            settings.Limited.Should().Equal(Permissions.RefundApprove);

            // Роли: + утверждение возвратов с лимитом 300, − отмена броней.
            var grants = settings.Grants.Where(g => g.Code != Permissions.BookingCancel).Append(new RoleGrantDto(Permissions.RefundApprove, 300m)).ToList();
            var saved = await owner.PutAsJsonAsync("/api/v1/roles/Administrator/permissions", new SetRolePermissionsRequest(grants), Ct);
            saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(Ct));

            foreach (var token in new[] { firstTokens.AccessToken, secondTokens.AccessToken })
            {
                var admin = _api.Authorized(token);
                var me = (await admin.GetFromJsonAsync<MeResponse>("/api/v1/me", Ct))!;
                me.Permissions.Should().Contain(Permissions.RefundApprove).And.NotContain(Permissions.BookingCancel);
                (await admin.GetFromJsonAsync<RefundPageDto>("/api/v1/refunds", Ct))!.ApproveLimit.Should().Be(300m);
            }

            // Личный лимит важнее лимита роли.
            var personal = await owner.PutAsJsonAsync($"/api/v1/staff/{first.Id}/permissions",
                new SetPermissionsRequest([new PermissionOverrideDto(Permissions.RefundApprove, true, 800m)]), Ct);
            personal.StatusCode.Should().Be(HttpStatusCode.OK, await personal.Content.ReadAsStringAsync(Ct));
            (await _api.Authorized(firstTokens.AccessToken).GetFromJsonAsync<RefundPageDto>("/api/v1/refunds", Ct))!.ApproveLimit.Should().Be(800m);
            (await _api.Authorized(secondTokens.AccessToken).GetFromJsonAsync<RefundPageDto>("/api/v1/refunds", Ct))!.ApproveLimit.Should().Be(300m);

            var staffView = (await owner.GetFromJsonAsync<StaffPermissionsDto>($"/api/v1/staff/{first.Id}/permissions", Ct))!;
            staffView.RoleLimits.Should().ContainSingle(l => l.Code == Permissions.RefundApprove && l.LimitAmount == 300m);
        }
        finally
        {
            await ResetAsync(owner, "Administrator");
        }

        var reset = (await owner.GetFromJsonAsync<RolePermissionsDto>("/api/v1/roles/Administrator/settings", Ct))!;
        reset.Grants.Select(g => g.Code).Should().BeEquivalentTo(reset.Defaults);
        (await _api.Authorized(secondTokens.AccessToken).GetFromJsonAsync<MeResponse>("/api/v1/me", Ct))!.Permissions
            .Should().Contain(Permissions.BookingCancel).And.NotContain(Permissions.RefundApprove);
    }

    [Fact]
    public async Task Seeder_DoesNotOverwriteOwnerSettings()
    {
        var owner = await OwnerAsync();
        try
        {
            var settings = (await owner.GetFromJsonAsync<RolePermissionsDto>("/api/v1/roles/Administrator/settings", Ct))!;
            var grants = settings.Grants.Append(new RoleGrantDto(Permissions.PaymentReverse, null)).ToList();
            (await owner.PutAsJsonAsync("/api/v1/roles/Administrator/permissions", new SetRolePermissionsRequest(grants), Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

            // Релиз: миграции и сидеры запускаются снова.
            await db.MigrateAsync(Ct);

            var after = (await owner.GetFromJsonAsync<RolePermissionsDto>("/api/v1/roles/Administrator/settings", Ct))!;
            after.Grants.Should().Contain(g => g.Code == Permissions.PaymentReverse);
        }
        finally
        {
            await ResetAsync(owner, "Administrator");
        }
    }

    [Fact]
    public async Task Housekeeper_OnlySafePermissions_OwnerRoleNotConfigurable()
    {
        var owner = await OwnerAsync();
        var (maid, _) = await _api.CreateActiveStaffAsync(Roles.Housekeeper);

        var settings = (await owner.GetFromJsonAsync<RolePermissionsDto>("/api/v1/roles/Housekeeper/settings", Ct))!;
        settings.Configurable.Select(p => p.Code).Should().BeEquivalentTo([Permissions.HousekeepingWork, Permissions.MaintenanceCreate]);

        var finance = await owner.PutAsJsonAsync("/api/v1/roles/Housekeeper/permissions",
            new SetRolePermissionsRequest([new RoleGrantDto(Permissions.HousekeepingWork, null), new RoleGrantDto(Permissions.PaymentView, null)]), Ct);
        (await ProblemCodeAsync(finance)).Should().Be("staff.permission_not_configurable");
        var guests = await owner.PutAsJsonAsync($"/api/v1/staff/{maid.Id}/permissions",
            new SetPermissionsRequest([new PermissionOverrideDto(Permissions.GuestView, true, null)]), Ct);
        (await ProblemCodeAsync(guests)).Should().Be("staff.permission_not_configurable");

        // Лично уборщику можно снять право сообщать о неисправностях.
        var revoke = await owner.PutAsJsonAsync($"/api/v1/staff/{maid.Id}/permissions",
            new SetPermissionsRequest([new PermissionOverrideDto(Permissions.MaintenanceCreate, false, null)]), Ct);
        revoke.StatusCode.Should().Be(HttpStatusCode.OK, await revoke.Content.ReadAsStringAsync(Ct));
        (await revoke.Content.ReadFromJsonAsync<StaffPermissionsDto>(Ct))!.Effective.Should().Equal(Permissions.HousekeepingWork);

        (await ProblemCodeAsync(await owner.GetAsync(new Uri("/api/v1/roles/Owner/settings", UriKind.Relative), Ct))).Should().Be("role.not_configurable");
        (await owner.GetAsync(new Uri("/api/v1/roles/Nobody/settings", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var (_, adminTokens) = await _api.CreateActiveStaffAsync(Roles.Administrator);
        var admin = _api.Authorized(adminTokens.AccessToken);
        (await admin.PostAsync(new Uri("/api/v1/roles/Administrator/permissions/reset", UriKind.Relative), null, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
