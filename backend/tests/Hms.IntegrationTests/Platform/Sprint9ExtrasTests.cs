using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hms.IntegrationTests.Bookings;
using Hms.Modules.Identity.Application;
using Hms.Modules.Identity.Contracts;
using Hms.Platform.Time;
using Microsoft.Extensions.DependencyInjection;
using static Hms.IntegrationTests.TestApi;

namespace Hms.IntegrationTests.Platform;

/// <summary>Доработки после Sprint 9: счётчик неподтверждённых броней, имена в журнале действий, возврат к брони после регистрации.</summary>
public class Sprint9ExtrasTests(PostgresFixture db)
{
    private readonly TestApi _api = new(db);

    private IHotelClock Clock => db.Factory.Services.GetRequiredService<IHotelClock>();

    [Fact]
    public async Task PendingCount_GrowsWithUnconfirmedBooking_AndNeedsBookingView()
    {
        var owner = _api.Authorized((await _api.LoginOwnerAsync()).AccessToken);
        async Task<int> CountAsync() => (await owner.GetFromJsonAsync<JsonElement>("/api/v1/bookings/pending-count", Ct)).GetProperty("pending").GetInt32();

        var before = await CountAsync();
        var room = await BookingTestKit.CreateRoomAsync(owner, 500m);
        await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(room.Id, Clock.Today.AddDays(20), 10, 2, confirm: false));
        (await CountAsync()).Should().BeGreaterThanOrEqualTo(before + 1);

        var (_, maid) = await _api.CreateActiveStaffAsync(Roles.Housekeeper);
        (await _api.Authorized(maid.AccessToken).GetAsync(new Uri("/api/v1/bookings/pending-count", UriKind.Relative), Ct)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AuditLog_ShowsStaffName()
    {
        var owner = _api.Authorized((await _api.LoginOwnerAsync()).AccessToken);
        var room = await BookingTestKit.CreateRoomAsync(owner, 500m);

        var page = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/audit-logs?entityType=Room&entityId={room.Id}&pageSize=10", Ct);
        var item = page.GetProperty("items").EnumerateArray().First(i => i.GetProperty("action").GetString() == "room.created");
        item.GetProperty("userName").GetString().Should().NotBeNullOrWhiteSpace();

        // Период в часовом поясе гостиницы (+06:00) — не ошибка 500, а записи за эти сутки.
        var day = Clock.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var next = Clock.Today.AddDays(1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var local = await owner.GetAsync(new Uri($"/api/v1/audit-logs?from={Uri.EscapeDataString(day + "T00:00:00+06:00")}&to={Uri.EscapeDataString(next + "T00:00:00+06:00")}&pageSize=5", UriKind.Relative), Ct);
        local.StatusCode.Should().Be(HttpStatusCode.OK, await local.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Register_WithReturnPath_PutsItIntoConfirmationLink_AndRejectsForeignUrl()
    {
        var email = UniqueEmail("return");
        var ok = await _api.Anonymous().PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest("Гость Возврат", email, null, GuestPassword, true, false, "/book?kind=hourly&roomTypeId=1"), Ct);
        ok.StatusCode.Should().Be(HttpStatusCode.Created, await ok.Content.ReadAsStringAsync(Ct));
        await db.TakeLinkAsync(email, "/confirm-email");
        var letter = db.Emails.Sent.Last(m => string.Equals(m.To, email, StringComparison.OrdinalIgnoreCase));
        letter.TextBody.Should().Contain("&next=%2Fbook%3Fkind%3Dhourly%26roomTypeId%3D1");

        foreach (var bad in new[] { "https://evil.example/x", "//evil.example", "book" })
        {
            var rejected = await _api.Anonymous().PostAsJsonAsync("/api/v1/auth/register",
                new RegisterRequest("Гость Чужой", UniqueEmail("bad"), null, GuestPassword, true, false, bad), Ct);
            rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest, bad);
        }
    }
}
