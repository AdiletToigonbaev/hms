using System.Net;
using System.Net.Http.Json;
using Hms.Modules.Hotel.Application;
using Hms.Modules.Identity.Application;
using Hms.Modules.Identity.Contracts;
using static Hms.IntegrationTests.TestApi;

namespace Hms.IntegrationTests.Hotel;

public class HotelTests(PostgresFixture db)
{
    private readonly TestApi _api = new(db);

    [Fact]
    public async Task Seed_CreatesHotel_WithDefaultSettings()
    {
        var owner = _api.Authorized((await _api.LoginOwnerAsync()).AccessToken);

        var hotel = await owner.GetFromJsonAsync<HotelDto>("/api/v1/hotel", Ct);
        var settings = await owner.GetFromJsonAsync<HotelSettingsDto>("/api/v1/hotel/settings", Ct);

        hotel!.TimeZone.Should().Be("Asia/Bishkek");
        hotel.Currency.Should().Be("KGS");
        settings!.OnlinePaymentEnabled.Should().BeFalse();
        settings.CancellationPolicy.FreeCancelHoursBeforeCheckIn.Should().Be(24);
        settings.PendingHoldHours.Should().Be(24);
    }

    [Fact]
    public async Task Administrator_CanReadSettings_ButNotChange()
    {
        var (_, admin) = await _api.CreateActiveStaffAsync(Roles.Administrator);
        var client = _api.Authorized(admin.AccessToken);

        var settings = await client.GetFromJsonAsync<HotelSettingsDto>("/api/v1/hotel/settings", Ct);
        var change = await client.PutAsJsonAsync("/api/v1/hotel/settings", ToUpdate(settings!), Ct);

        change.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync(new Uri("/api/v1/settings/integrations", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateHotel_StaleVersion_Returns409()
    {
        var owner = _api.Authorized((await _api.LoginOwnerAsync()).AccessToken);
        var hotel = (await owner.GetFromJsonAsync<HotelDto>("/api/v1/hotel", Ct))!;

        var first = await owner.PutAsJsonAsync("/api/v1/hotel",
            new UpdateHotelRequest(hotel.Name, "г. Бишкек, ул. Тестовая, 1", "0555 123 456", "info@hotel.kg", null, hotel.Version), Ct);
        var stale = await owner.PutAsJsonAsync("/api/v1/hotel",
            new UpdateHotelRequest(hotel.Name, "Другой адрес", null, null, null, hotel.Version), Ct);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        (await first.Content.ReadFromJsonAsync<HotelDto>(Ct))!.Phone.Should().Be("+996555123456");
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemCodeAsync(stale)).Should().Be("concurrency.conflict");
    }

    [Fact]
    public async Task UpdateSettings_Invalid_Returns400WithFieldErrors()
    {
        var owner = _api.Authorized((await _api.LoginOwnerAsync()).AccessToken);
        var settings = (await owner.GetFromJsonAsync<HotelSettingsDto>("/api/v1/hotel/settings", Ct))!;

        var response = await owner.PutAsJsonAsync("/api/v1/hotel/settings",
            ToUpdate(settings) with { PrepaymentPercent = 150, CheckInTime = "25:99" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(Ct);
        body.Should().Contain("prepaymentPercent").And.Contain("checkInTime");
    }

    [Fact]
    public async Task HourlyRules_AreSettings_WithAgreedDefaults_AndValidated()
    {
        var owner = _api.Authorized((await _api.LoginOwnerAsync()).AccessToken);
        var settings = (await owner.GetFromJsonAsync<HotelSettingsDto>("/api/v1/hotel/settings", Ct))!;

        // Значения по умолчанию — решение заказчика (roadmap §6.1a); тесты могли их поменять, поэтому проверяем только при первом чтении формы
        settings.HourlyPolicy.Should().NotBeNull();
        settings.HourlyMinHours.Should().BeInRange(1, 24);

        var invalid = await owner.PutAsJsonAsync("/api/v1/hotel/settings",
            ToUpdate(settings) with { HourlyPolicy = settings.HourlyPolicy with { NoShowAfterMinutes = 0, LateCancelPenaltyHours = 99 } }, Ct);
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await invalid.Content.ReadAsStringAsync(Ct)).Should().Contain("noShowAfterMinutes").And.Contain("lateCancelPenaltyHours");

        var changed = settings.HourlyPolicy with { FreeCancelMinutesBeforeStart = 180, NoShowAfterMinutes = 45, RefundUnusedOnEarlyDeparture = true };
        var ok = await owner.PutAsJsonAsync("/api/v1/hotel/settings", ToUpdate(settings) with { HourlyPolicy = changed }, Ct);
        ok.StatusCode.Should().Be(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync(Ct));
        (await ok.Content.ReadFromJsonAsync<HotelSettingsDto>(Ct))!.HourlyPolicy.Should().BeEquivalentTo(changed);

        // Возврат исходных значений, чтобы не влиять на другие тесты
        var current = (await owner.GetFromJsonAsync<HotelSettingsDto>("/api/v1/hotel/settings", Ct))!;
        (await owner.PutAsJsonAsync("/api/v1/hotel/settings", ToUpdate(current) with { HourlyPolicy = settings.HourlyPolicy }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void Policies_Defaults_MatchCustomerDecision_NoPenalties()
    {
        // Решение заказчика 2026-11-21: штрафов по умолчанию нет — владелец включит сам.
        new Hms.Modules.Hotel.Domain.CancellationPolicy().Should().BeEquivalentTo(new
        {
            FreeCancelHoursBeforeCheckIn = 24,
            LateCancelPenaltyNights = 0,
            NoShowPenaltyNights = 0,
            EarlyDeparturePenaltyNights = 0,
        });
        new Hms.Modules.Hotel.Domain.HourlyPolicy().Should().BeEquivalentTo(new
        {
            FreeCancelMinutesBeforeStart = 120,
            LateCancelPenaltyHours = 0,
            NoShowAfterMinutes = 30,
            NoShowPenaltyHours = 0,
            PendingHoldMinutes = 30,
            RefundUnusedOnEarlyDeparture = false,
        });
    }

    [Fact]
    public async Task PublicHotelInfo_IsAnonymous_AndHidesInternalSettings()
    {
        var response = await _api.Anonymous().GetAsync(new Uri("/api/v1/public/hotel-info", UriKind.Relative), Ct);
        var json = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.Should().Contain("checkInTime").And.NotContain("pendingHoldHours").And.NotContain("onlinePaymentEnabled");
    }

    [Fact]
    public async Task Integrations_Banks_CannotBeEnabledWithoutAccess_WhatsAppNeedsPhoneNumberId()
    {
        var owner = _api.Authorized((await _api.LoginOwnerAsync()).AccessToken);

        var list = await owner.GetFromJsonAsync<List<IntegrationDto>>("/api/v1/settings/integrations", Ct);
        list!.Select(i => i.Provider).Should().BeEquivalentTo(["MBank", "Optima", "WhatsApp", "Email"]);
        list!.Single(i => i.Provider == "MBank").Availability.Should().Be("awaiting_access");

        foreach (var provider in new[] { "MBank", "Optima" })
        {
            var enable = await owner.PutAsJsonAsync($"/api/v1/settings/integrations/{provider}",
                new UpdateIntegrationRequest(true, "Sandbox", null), Ct);
            enable.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, provider);
            (await ProblemCodeAsync(enable)).Should().Be("integration.not_available");
            (await owner.PostAsync(new Uri($"/api/v1/settings/integrations/{provider}/test", UriKind.Relative), null, Ct))
                .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        }

        // WhatsApp: адаптер есть (Sprint 10), ключи в тестах заданы — без Phone Number ID включить нельзя.
        // Тесты сообщений включают WhatsApp (общая коллекция «whatsapp» — не параллельно), поэтому сначала выключаем.
        (await owner.PutAsJsonAsync("/api/v1/settings/integrations/WhatsApp", new UpdateIntegrationRequest(false, "Sandbox", null), Ct)).EnsureSuccessStatusCode();
        var whatsApp = await owner.PutAsJsonAsync("/api/v1/settings/integrations/WhatsApp", new UpdateIntegrationRequest(true, "Sandbox", null), Ct);
        whatsApp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(whatsApp)).Should().Be("integration.phone_number_id_required");
    }

    [Fact]
    public async Task Integrations_PublicConfig_RejectsSecretLikeKeys()
    {
        var owner = _api.Authorized((await _api.LoginOwnerAsync()).AccessToken);

        var ok = await owner.PutAsJsonAsync("/api/v1/settings/integrations/MBank",
            new UpdateIntegrationRequest(false, "Sandbox", new Dictionary<string, string> { ["merchantId"] = "M-123" }), Ct);
        var secret = await owner.PutAsJsonAsync("/api/v1/settings/integrations/MBank",
            new UpdateIntegrationRequest(false, "Sandbox", new Dictionary<string, string> { ["secretKey"] = "do-not-store" }), Ct);

        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ok.Content.ReadFromJsonAsync<IntegrationDto>(Ct))!.PublicConfig["merchantId"].Should().Be("M-123");
        secret.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(secret)).Should().Be("integration.key_not_allowed");
    }

    [Fact]
    public async Task EmailIntegration_Test_SendsLetterToOwner()
    {
        var owner = _api.Authorized((await _api.LoginOwnerAsync()).AccessToken);

        var response = await owner.PostAsync(new Uri("/api/v1/settings/integrations/Email/test", UriKind.Relative), null, Ct);
        await db.ProcessOutboxAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        db.Emails.Sent.Should().Contain(m => m.To == PostgresFixture.OwnerEmail && m.Subject == "Проверка отправки писем");
    }

    internal static UpdateHotelSettingsRequest ToUpdate(HotelSettingsDto s) => new(
        s.CheckInTime, s.CheckOutTime, s.PrepaymentPercent, s.PaymentHoldMinutes, s.PendingHoldHours,
        s.AutoConfirmWebBookings, s.AutoReleaseRoomAfterCleaning, s.RequireEmailConfirmation,
        s.NoShowCutoffTime, s.HourlyMinHours, s.HourlyMaxHours, s.CleaningBufferMinutes, s.CancellationPolicy, s.Rules, s.Version, s.HourlyPolicy);
}

/// <summary>Тесты, меняющие глобальные настройки гостиницы, — не параллельно с остальными.</summary>
[CollectionDefinition(nameof(GlobalSettingsGroup), DisableParallelization = true)]
public sealed class GlobalSettingsGroup;

[Collection(nameof(GlobalSettingsGroup))]
public class HotelSettingsEffectTests(PostgresFixture db)
{
    private readonly TestApi _api = new(db);

    [Fact]
    public async Task RequireEmailConfirmation_Off_GuestCanLoginWithoutConfirmation()
    {
        var owner = _api.Authorized((await _api.LoginOwnerAsync()).AccessToken);
        var settings = (await owner.GetFromJsonAsync<HotelSettingsDto>("/api/v1/hotel/settings", Ct))!;
        var off = await owner.PutAsJsonAsync("/api/v1/hotel/settings", HotelTests.ToUpdate(settings) with { RequireEmailConfirmation = false }, Ct);
        off.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await off.Content.ReadFromJsonAsync<HotelSettingsDto>(Ct))!;
        try
        {
            var email = UniqueEmail("noconfirm");
            var register = await _api.Anonymous().PostAsJsonAsync("/api/v1/auth/register",
                new RegisterRequest("Без подтверждения", email, null, GuestPassword, true, false), Ct);
            (await register.Content.ReadFromJsonAsync<RegisterResponse>(Ct))!.EmailConfirmationRequired.Should().BeFalse();

            (await _api.LoginRawAsync(email, GuestPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            (await owner.PutAsJsonAsync("/api/v1/hotel/settings", HotelTests.ToUpdate(updated) with { RequireEmailConfirmation = true }, Ct))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}
