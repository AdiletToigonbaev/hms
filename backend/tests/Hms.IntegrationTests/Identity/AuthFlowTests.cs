using System.Net;
using System.Net.Http.Json;
using Hms.Modules.Identity.Application;
using static Hms.IntegrationTests.TestApi;

namespace Hms.IntegrationTests.Identity;

public class AuthFlowTests(PostgresFixture db)
{
    private readonly TestApi _api = new(db);

    [Fact]
    public async Task Register_ConfirmEmail_Login_Me_FullFlow()
    {
        var email = UniqueEmail("flow");
        var phone = UniquePhone();
        var register = await _api.Anonymous().PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest("Айгуль Токтогулова", email, phone, GuestPassword, true, true), Ct);
        register.StatusCode.Should().Be(HttpStatusCode.Created);
        (await register.Content.ReadFromJsonAsync<RegisterResponse>(Ct))!.EmailConfirmationRequired.Should().BeTrue();

        // До подтверждения email вход запрещён (после проверки пароля — без раскрытия аккаунтов)
        var early = await _api.LoginRawAsync(email, GuestPassword);
        early.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ProblemCodeAsync(early)).Should().Be("auth.email_not_confirmed");

        var (userId, token) = await db.TakeLinkAsync(email, "/confirm-email");
        (await _api.Anonymous().PostAsJsonAsync("/api/v1/auth/confirm-email", new ConfirmEmailRequest(userId, token), Ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Вход по телефону в национальном формате
        var tokens = await _api.LoginAsync("0" + phone[4..], GuestPassword);
        tokens.User.Id.Should().Be(userId);
        tokens.User.Roles.Should().Equal("Guest");
        tokens.User.Permissions.Should().BeEmpty();
        tokens.User.EmailConfirmed.Should().BeTrue();

        var me = await _api.Authorized(tokens.AccessToken).GetFromJsonAsync<MeResponse>("/api/v1/me", Ct);
        me!.Email.Should().Be(email);
        me.Phone.Should().Be(phone);
    }

    [Fact]
    public async Task Register_Validation_ReturnsFieldErrors()
    {
        var response = await _api.Anonymous().PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest("", "not-an-email", "12", "short", AcceptPersonalData: false, AcceptWhatsApp: false), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(Ct);
        body.GetProperty("code").GetString().Should().Be("validation.failed");
        var errors = body.GetProperty("errors");
        foreach (var field in new[] { "fullName", "email", "phone", "password", "acceptPersonalData" })
        {
            errors.TryGetProperty(field, out _).Should().BeTrue($"ожидается ошибка поля {field}");
        }
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns409()
    {
        var (email, _) = await _api.RegisterConfirmedGuestAsync();

        var again = await _api.Anonymous().PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest("Другой", email.ToUpperInvariant(), null, GuestPassword, true, false), Ct);

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemCodeAsync(again)).Should().Be("auth.email_taken");
    }

    [Fact]
    public async Task Register_ConcurrentSameEmail_ExactlyOneSucceeds()
    {
        var email = UniqueEmail("race");
        var attempts = Enumerable.Range(0, 8).Select(_ => _api.Anonymous().PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest("Гонка", email, null, GuestPassword, true, false), Ct));

        var responses = await Task.WhenAll(attempts);

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Where(r => r.StatusCode != HttpStatusCode.Created).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Login_WrongPassword_And_UnknownUser_AreIndistinguishable()
    {
        var (email, _) = await _api.RegisterConfirmedGuestAsync();

        var wrong = await _api.LoginRawAsync(email, "wrong-password-000");
        var unknown = await _api.LoginRawAsync(UniqueEmail("nobody"), "wrong-password-000");

        wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        unknown.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ProblemCodeAsync(wrong)).Should().Be("auth.invalid_credentials");
        (await ProblemCodeAsync(unknown)).Should().Be("auth.invalid_credentials");
    }

    [Fact]
    public async Task Login_FiveFailures_LocksAccount_EvenForCorrectPassword()
    {
        var (email, _) = await _api.RegisterConfirmedGuestAsync();

        for (var i = 0; i < 4; i++)
        {
            (await _api.LoginRawAsync(email, "wrong-password-000")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var fifth = await _api.LoginRawAsync(email, "wrong-password-000");
        (await ProblemCodeAsync(fifth)).Should().Be("auth.locked_out");

        var correct = await _api.LoginRawAsync(email, GuestPassword);
        correct.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ProblemCodeAsync(correct)).Should().Be("auth.locked_out");
    }

    [Fact]
    public async Task Refresh_RotatesTokens_AndReuseRevokesWholeSession()
    {
        var (email, _) = await _api.RegisterConfirmedGuestAsync();
        var first = await _api.LoginAsync(email, GuestPassword);

        var rotated = await Refresh(first.RefreshToken);
        rotated.StatusCode.Should().Be(HttpStatusCode.OK);
        var second = (await rotated.Content.ReadFromJsonAsync<TokenResponse>(Ct))!;
        second.RefreshToken.Should().NotBe(first.RefreshToken);

        // Сразу после ротации повтор старого токена — гонка вкладок: отказ без завершения сессии
        var race = await Refresh(first.RefreshToken);
        (await ProblemCodeAsync(race)).Should().Be("auth.refresh_token_rotated");
        (await Refresh(second.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Refresh_ReuseOfOldToken_AfterGracePeriod_RevokesFamily()
    {
        var (email, userId) = await _api.RegisterConfirmedGuestAsync();
        var first = await _api.LoginAsync(email, GuestPassword);
        var second = (await (await Refresh(first.RefreshToken)).Content.ReadFromJsonAsync<TokenResponse>(Ct))!;

        // Имитируем, что ротация была давно (кража токена обнаружена позже окна гонки)
        await ExecSqlAsync($"UPDATE identity.refresh_tokens SET revoked_at = now() - interval '1 hour' WHERE user_id = '{userId}' AND revoke_reason = 'Rotated'");

        var reuse = await Refresh(first.RefreshToken);
        reuse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ProblemCodeAsync(reuse)).Should().Be("auth.refresh_token_invalid");

        // Вся сессия завершена: свежий токен злоумышленника/владельца тоже недействителен
        (await Refresh(second.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_ParallelWithSameToken_OnlyOneSucceeds()
    {
        var (email, _) = await _api.RegisterConfirmedGuestAsync();
        var tokens = await _api.LoginAsync(email, GuestPassword);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Refresh(tokens.RefreshToken)));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        foreach (var r in responses.Where(r => r.StatusCode != HttpStatusCode.OK))
        {
            (await ProblemCodeAsync(r)).Should().Be("auth.refresh_token_rotated");
        }
    }

    [Fact]
    public async Task Logout_RevokesRefreshToken_AndIsIdempotent()
    {
        var (email, _) = await _api.RegisterConfirmedGuestAsync();
        var tokens = await _api.LoginAsync(email, GuestPassword);

        (await _api.Anonymous().PostAsJsonAsync("/api/v1/auth/logout", new LogoutRequest(tokens.RefreshToken), Ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _api.Anonymous().PostAsJsonAsync("/api/v1/auth/logout", new LogoutRequest(tokens.RefreshToken), Ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Refresh(tokens.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ForgotAndResetPassword_ChangesPassword_AndEndsSessions()
    {
        var (email, _) = await _api.RegisterConfirmedGuestAsync();
        var session = await _api.LoginAsync(email, GuestPassword);

        (await _api.Anonymous().PostAsJsonAsync("/api/v1/auth/forgot-password", new EmailRequest(email), Ct))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        var (userId, token) = await db.TakeLinkAsync(email, "/reset-password");

        (await _api.Anonymous().PostAsJsonAsync("/api/v1/auth/reset-password", new ResetPasswordRequest(userId, token, "new-guest-password-1"), Ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await _api.LoginRawAsync(email, GuestPassword)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await _api.LoginAsync(email, "new-guest-password-1");
        (await Refresh(session.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _api.Authorized(session.AccessToken).GetAsync(new Uri("/api/v1/me", UriKind.Relative), Ct))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized, "старый access-токен отзывается сменой SecurityStamp");

        // Ссылка одноразовая
        (await _api.Anonymous().PostAsJsonAsync("/api/v1/auth/reset-password", new ResetPasswordRequest(userId, token, "another-password-22"), Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ForgotPassword_UnknownEmail_ReturnsSameResponse_AndSendsNothing()
    {
        var email = UniqueEmail("ghost");

        var response = await _api.Anonymous().PostAsJsonAsync("/api/v1/auth/forgot-password", new EmailRequest(email), Ct);
        await db.ProcessOutboxAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        db.Emails.Sent.Should().NotContain(m => m.To == email);
    }

    [Fact]
    public async Task ConfirmEmail_InvalidToken_Returns400()
    {
        var response = await _api.Anonymous().PostAsJsonAsync("/api/v1/auth/confirm-email",
            new ConfirmEmailRequest(Guid.NewGuid(), "forged-token"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(response)).Should().Be("auth.invalid_token");
    }

    private Task<HttpResponseMessage> Refresh(string refreshToken) =>
        _api.Anonymous().PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(refreshToken), Ct);

    private async Task ExecSqlAsync(string sql)
    {
        await using var connection = new Npgsql.NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync(Ct);
#pragma warning disable CA2100 // тестовые данные — сгенерированные Guid
        await using var command = new Npgsql.NpgsqlCommand(sql, connection);
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync(Ct);
    }
}
