using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hms.Modules.Bookings.Application;
using Hms.Modules.Guests.Application;
using Hms.Modules.Identity.Application;
using Hms.Modules.Identity.Contracts;
using Hms.Modules.Rooms.Application;
using Hms.Platform.Time;
using Hms.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Hms.IntegrationTests.TestApi;

namespace Hms.IntegrationTests.Guests;

public class GuestsTests(PostgresFixture db)
{
    private readonly TestApi _api = new(db);

    private DateOnly Today => db.Factory.Services.GetRequiredService<IHotelClock>().Today;

    private async Task<HttpClient> OwnerAsync() => _api.Authorized((await _api.LoginOwnerAsync()).AccessToken);

    private static SaveGuestRequest NewGuest(string? phone = null, string? email = null, string name = "Айгуль Тестова") =>
        new(name, phone ?? UniquePhone(), null, email, null, "Любит тихий номер", null);

    private static async Task<GuestDto> CreateGuestAsync(HttpClient client, SaveGuestRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/v1/guests", request, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<GuestDto>(Ct))!;
    }

    private static async Task<Guid> CreateRoomAsync(HttpClient client)
    {
        var type = await client.PostAsJsonAsync("/api/v1/room-types",
            new SaveRoomTypeRequest($"Гости {Guid.NewGuid():N}"[..20], null, 2, 1, "Double", 3000m, 10, null, HourlyPrice: 500m), Ct);
        var typeId = (await type.Content.ReadFromJsonAsync<RoomTypeDto>(Ct))!.Id;
        var room = await client.PostAsJsonAsync("/api/v1/rooms",
            new SaveRoomRequest(typeId, Random.Shared.Next(1000, 999_999).ToString(System.Globalization.CultureInfo.InvariantCulture), 1, null, null, null, null, null, null), Ct);
        return (await room.Content.ReadFromJsonAsync<RoomDto>(Ct))!.Id;
    }

    private static async Task<BookingDto> BookAsync(HttpClient client, Guid roomId, int dayOffset, DateOnly today, string? phone = null, Guid? guestId = null, string name = "Бакыт Гостевой")
    {
        var request = new CreateBookingRequest(roomId, null, null, 2, 0, guestId is null ? name : "", phone, null, null, null, null, true,
            Kind: StayKinds.Hourly, Date: today.AddDays(dayOffset), StartHour: 10, Hours: 2, GuestId: guestId);
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/bookings") { Content = JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var response = await client.SendAsync(message, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<BookingDto>(Ct))!;
    }

    [Fact]
    public async Task CreateSearchUpdate_WithDuplicateDetection_AndStaleVersion()
    {
        var owner = await OwnerAsync();
        var phone = UniquePhone();
        var guest = await CreateGuestAsync(owner, NewGuest(phone, email: $"G{Guid.NewGuid():N}@Test.Local"));
        guest.Source.Should().Be("Admin");
        guest.Email.Should().Be(guest.Email!.ToLowerInvariant());

        var duplicate = await owner.PostAsJsonAsync("/api/v1/guests", NewGuest(phone, name: "Другое имя"), Ct);
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync(Ct)).RootElement;
        problem.GetProperty("code").GetString().Should().Be("guest.duplicate");
        problem.GetProperty("guestId").GetGuid().Should().Be(guest.Id);

        // Поиск по части номера (только цифры) и по имени
        var digits = phone[^6..];
        var byPhone = (await owner.GetFromJsonAsync<PagedResult<GuestListItemDto>>($"/api/v1/guests?search={digits}", Ct))!;
        byPhone.Items.Should().ContainSingle(g => g.Id == guest.Id);

        var updated = await owner.PutAsJsonAsync($"/api/v1/guests/{guest.Id}",
            new SaveGuestRequest("Айгуль Обновлённая", phone, phone, guest.Email, new DateOnly(1990, 5, 1), "VIP", guest.Version), Ct);
        updated.StatusCode.Should().Be(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync(Ct));
        (await updated.Content.ReadFromJsonAsync<GuestDto>(Ct))!.Should().BeEquivalentTo(new { FullName = "Айгуль Обновлённая", WhatsAppPhone = phone, Notes = "VIP" });

        var stale = await owner.PutAsJsonAsync($"/api/v1/guests/{guest.Id}", NewGuest(phone) with { Version = guest.Version }, Ct);
        (await ProblemCodeAsync(stale)).Should().Be("concurrency.conflict");

        // В аудите — названия полей, а не персональные данные
        await using var scope = db.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DbContext>();
        var after = await context.Database.SqlQuery<string>(
            $"SELECT after::text AS \"Value\" FROM audit.audit_logs WHERE action = 'guest.updated' AND entity_id = {guest.Id.ToString()} LIMIT 1").SingleAsync(Ct);
        after.Should().Contain("fullName").And.NotContain("Обновлённая").And.NotContain(phone);
    }

    [Fact]
    public async Task Booking_CreatesOrReusesGuestCard_AndCardShowsHistoryAndStats()
    {
        var owner = await OwnerAsync();
        var room = await CreateRoomAsync(owner);
        var phone = UniquePhone();

        var first = await BookAsync(owner, room, 50, Today, phone);
        var second = await BookAsync(owner, room, 51, Today, phone, name: "Бакыт (второй раз)");

        first.GuestId.Should().NotBeNull();
        second.GuestId.Should().Be(first.GuestId, "гость найден по телефону");
        var card = (await owner.GetFromJsonAsync<GuestDto>($"/api/v1/guests/{first.GuestId}", Ct))!;
        card.Source.Should().Be("Booking");
        card.Stats.Should().BeEquivalentTo(new { BookingsCount = 2, VisitsCount = 0, BookedAmount = 2000m });

        var history = (await owner.GetFromJsonAsync<List<GuestBookingDto>>($"/api/v1/guests/{first.GuestId}/bookings", Ct))!;
        history.Select(b => b.Number).Should().BeEquivalentTo([first.Number, second.Number]);

        // Бронь на выбранного из базы гостя — контакт берётся из карточки
        var third = await BookAsync(owner, room, 52, Today, guestId: first.GuestId);
        third.GuestId.Should().Be(first.GuestId);
        third.GuestPhone.Should().Be(phone);
        third.GuestName.Should().Be(card.FullName);

        var lookup = (await owner.GetFromJsonAsync<List<GuestLookupDto>>($"/api/v1/guests/lookup?q={phone[^5..]}", Ct))!;
        lookup.Should().ContainSingle(g => g.Id == first.GuestId).Which.BookingsCount.Should().Be(3);
    }

    [Fact]
    public async Task PortalRegistration_CreatesCardWithConsents_AndLinksExistingBookingGuest()
    {
        var owner = await OwnerAsync();
        var room = await CreateRoomAsync(owner);
        var phone = UniquePhone();
        var booking = await BookAsync(owner, room, 60, Today, phone);

        var email = UniqueEmail("portal");
        var register = await _api.Anonymous().PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest("Бакыт Порталов", email, phone, GuestPassword, AcceptPersonalData: true, AcceptWhatsApp: true), Ct);
        register.StatusCode.Should().Be(HttpStatusCode.Created, await register.Content.ReadAsStringAsync(Ct));

        var card = (await owner.GetFromJsonAsync<GuestDto>($"/api/v1/guests/{booking.GuestId}", Ct))!;
        card.HasAccount.Should().BeTrue("карточка, созданная при брони, привязана к аккаунту по телефону");
        card.Email.Should().Be(email);
        card.Consents.Should().BeEquivalentTo([new { Type = "PersonalData", Granted = true, Source = "Portal" }, new { Type = "WhatsApp", Granted = true, Source = "Portal" }]);

        // Обезличить гостя с аккаунтом пока нельзя — аккаунт удаляется в личном кабинете (Sprint 8)
        var anonymize = await owner.PostAsync(new Uri($"/api/v1/guests/{booking.GuestId}/anonymize", UriKind.Relative), null, Ct);
        (await ProblemCodeAsync(anonymize)).Should().Be("guest.has_account");
    }

    [Fact]
    public async Task Consents_AreJournaled_WithSourceAndAuthor_AndFilterable()
    {
        var owner = await OwnerAsync();
        var guest = await CreateGuestAsync(owner, NewGuest());

        (await owner.PostAsJsonAsync($"/api/v1/guests/{guest.Id}/consents", new SetConsentRequest("WhatsApp", true, "FrontDesk"), Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        var filtered = (await owner.GetFromJsonAsync<PagedResult<GuestListItemDto>>($"/api/v1/guests?filter=whatsApp&search={guest.Phone![^7..]}", Ct))!;
        filtered.Items.Should().ContainSingle(g => g.Id == guest.Id).Which.WhatsAppConsent.Should().BeTrue();

        var revoked = await owner.PostAsJsonAsync($"/api/v1/guests/{guest.Id}/consents", new SetConsentRequest("WhatsApp", false, "Phone"), Ct);
        (await revoked.Content.ReadFromJsonAsync<GuestDto>(Ct))!.Consents.Should().ContainSingle(c => c.Type == "WhatsApp").Which.Granted.Should().BeFalse();

        var history = (await owner.GetFromJsonAsync<List<ConsentHistoryDto>>($"/api/v1/guests/{guest.Id}/consents", Ct))!;
        history.Select(h => (h.Granted, h.Source)).Should().Equal((false, "Phone"), (true, "FrontDesk"));
        history.Should().OnlyContain(h => h.RecordedByName == "Тестовый владелец");

        var invalid = await owner.PostAsJsonAsync($"/api/v1/guests/{guest.Id}/consents", new SetConsentRequest("WhatsApp", true, "Telepathy"), Ct);
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Anonymize_ByOwner_RemovesPersonalDataEverywhere_KeepsStats()
    {
        var owner = await OwnerAsync();
        var room = await CreateRoomAsync(owner);
        var phone = UniquePhone();
        var booking = await BookAsync(owner, room, 70, Today, phone, name: "Секрет Секретов");
        await owner.PutAsJsonAsync($"/api/v1/bookings/{booking.Id}",
            new UpdateBookingRequest("Секрет Изменённый", phone, null, 2, 0, null, booking.Version), Ct);

        var (_, adminTokens) = await _api.CreateActiveStaffAsync(Roles.Administrator);
        var admin = _api.Authorized(adminTokens.AccessToken);
        (await admin.PostAsync(new Uri($"/api/v1/guests/{booking.GuestId}/anonymize", UriKind.Relative), null, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "обезличивание — только владелец");

        var result = await owner.PostAsync(new Uri($"/api/v1/guests/{booking.GuestId}/anonymize", UriKind.Relative), null, Ct);
        result.StatusCode.Should().Be(HttpStatusCode.OK, await result.Content.ReadAsStringAsync(Ct));
        var card = (await result.Content.ReadFromJsonAsync<GuestDto>(Ct))!;
        card.Should().BeEquivalentTo(new { IsAnonymized = true, FullName = "Гость удалён", Phone = (string?)null, Email = (string?)null, Notes = (string?)null });
        card.Stats.BookingsCount.Should().Be(1, "цифры по броням остаются");

        var bookingAfter = (await owner.GetFromJsonAsync<BookingDto>($"/api/v1/bookings/{booking.Id}", Ct))!;
        bookingAfter.GuestName.Should().Be("Гость удалён");
        bookingAfter.GuestPhone.Should().BeNull();
        var historyJson = await owner.GetStringAsync($"/api/v1/bookings/{booking.Id}/history", Ct);
        historyJson.Should().NotContain("Секрет").And.NotContain(phone);

        var list = (await owner.GetFromJsonAsync<PagedResult<GuestListItemDto>>($"/api/v1/guests?search={phone[^6..]}", Ct))!;
        list.Items.Should().NotContain(g => g.Id == booking.GuestId);

        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/bookings")
        {
            Content = JsonContent.Create(new CreateBookingRequest(room, null, null, 1, 0, "", null, null, null, null, null, true,
                Kind: StayKinds.Hourly, Date: Today.AddDays(71), StartHour: 10, Hours: 2, GuestId: booking.GuestId)),
        };
        message.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        (await ProblemCodeAsync(await owner.SendAsync(message, Ct))).Should().Be("guest.anonymized");
    }

    [Fact]
    public async Task Permissions_HousekeeperAndGuestHaveNoAccess_AdminManages()
    {
        var owner = await OwnerAsync();
        var guest = await CreateGuestAsync(owner, NewGuest());
        var (_, maidTokens) = await _api.CreateActiveStaffAsync(Roles.Housekeeper);
        var (guestEmail, _) = await _api.RegisterConfirmedGuestAsync();
        var (_, adminTokens) = await _api.CreateActiveStaffAsync(Roles.Administrator);
        var maid = _api.Authorized(maidTokens.AccessToken);
        var portalGuest = _api.Authorized((await _api.LoginAsync(guestEmail, GuestPassword)).AccessToken);
        var admin = _api.Authorized(adminTokens.AccessToken);

        foreach (var client in new[] { maid, portalGuest })
        {
            (await client.GetAsync(new Uri("/api/v1/guests", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await client.GetAsync(new Uri($"/api/v1/guests/{guest.Id}", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await client.GetAsync(new Uri("/api/v1/guests/lookup?q=Айгуль", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await client.PostAsJsonAsync("/api/v1/guests", NewGuest(), Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        (await admin.GetAsync(new Uri($"/api/v1/guests/{guest.Id}", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.PostAsJsonAsync("/api/v1/guests", NewGuest(), Ct)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await admin.GetAsync(new Uri($"/api/v1/guests/{Guid.NewGuid()}", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
