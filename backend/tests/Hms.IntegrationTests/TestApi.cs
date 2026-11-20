using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Hms.Modules.Identity.Application;

namespace Hms.IntegrationTests;

/// <summary>Помощники для HTTP-сценариев: регистрация, вход, создание сотрудников.</summary>
public sealed class TestApi(PostgresFixture db)
{
    public const string GuestPassword = "guest-password-123";
    public const string StaffInitialPassword = "staff-initial-pass";
    public const string StaffPassword = "staff-final-password";

    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public HttpClient Anonymous() => db.Factory.CreateClient();

    public HttpClient Authorized(string accessToken)
    {
        var client = db.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    public static string UniqueEmail(string prefix = "user") => $"{prefix}-{Guid.NewGuid():N}@test.local";

    /// <summary>Уникальный номер +996 7XX XXX XXX.</summary>
    public static string UniquePhone() => "+9967" + Random.Shared.NextInt64(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

    public async Task<HttpResponseMessage> LoginRawAsync(string login, string password) =>
        await Anonymous().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(login, password), Ct);

    public async Task<TokenResponse> LoginAsync(string login, string password)
    {
        var response = await LoginRawAsync(login, password);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<TokenResponse>(Ct))!;
    }

    public Task<TokenResponse> LoginOwnerAsync() => LoginAsync(PostgresFixture.OwnerEmail, PostgresFixture.OwnerPassword);

    /// <summary>Регистрирует гостя и подтверждает email по ссылке из письма.</summary>
    public async Task<(string Email, Guid UserId)> RegisterConfirmedGuestAsync()
    {
        var email = UniqueEmail("guest");
        var response = await Anonymous().PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterRequest("Гость Тестовый", email, null, GuestPassword, AcceptPersonalData: true, AcceptWhatsApp: false),
            Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));

        var (userId, token) = await db.TakeLinkAsync(email, "/confirm-email");
        var confirm = await Anonymous().PostAsJsonAsync("/api/v1/auth/confirm-email", new ConfirmEmailRequest(userId, token), Ct);
        confirm.StatusCode.Should().Be(HttpStatusCode.NoContent);
        return (email, userId);
    }

    /// <summary>Владелец создаёт сотрудника; сотрудник входит и меняет временный пароль.</summary>
    public async Task<(StaffDto Staff, TokenResponse Tokens)> CreateActiveStaffAsync(string role)
    {
        var owner = await LoginOwnerAsync();
        var email = UniqueEmail(role.ToLowerInvariant());
        var created = await Authorized(owner.AccessToken).PostAsJsonAsync(
            "/api/v1/staff",
            new CreateStaffRequest($"Сотрудник {role}", email, null, role, StaffInitialPassword),
            Ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var staff = (await created.Content.ReadFromJsonAsync<StaffDto>(Ct))!;

        var first = await LoginAsync(email, StaffInitialPassword);
        var changed = await Authorized(first.AccessToken).PostAsJsonAsync(
            "/api/v1/me/change-password", new ChangePasswordRequest(StaffInitialPassword, StaffPassword), Ct);
        changed.StatusCode.Should().Be(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync(Ct));
        return (staff, (await changed.Content.ReadFromJsonAsync<TokenResponse>(Ct))!);
    }

    public static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync(Ct);
        return string.IsNullOrEmpty(json) ? null : JsonDocument.Parse(json).RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
    }
}
