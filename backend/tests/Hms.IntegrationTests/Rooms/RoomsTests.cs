using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Hms.Modules.Identity.Contracts;
using Hms.Modules.Rooms.Application;
using static Hms.IntegrationTests.TestApi;

namespace Hms.IntegrationTests.Rooms;

public class RoomsTests(PostgresFixture db)
{
    // Минимальный корректный PNG 1×1.
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private readonly TestApi _api = new(db);

    private async Task<HttpClient> OwnerAsync() => _api.Authorized((await _api.LoginOwnerAsync()).AccessToken);

    private static string UniqueNumber() => Random.Shared.Next(1000, 999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<RoomTypeDto> CreateTypeAsync(HttpClient client, decimal price = 3500m, string? name = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/room-types",
            new SaveRoomTypeRequest(name ?? $"Стандарт {Guid.NewGuid():N}"[..20], "Уютный номер", 2, 1, "Double", price, 10, null), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<RoomTypeDto>(Ct))!;
    }

    private static async Task<RoomDto> CreateRoomAsync(HttpClient client, Guid typeId, decimal? price = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/rooms",
            new SaveRoomRequest(typeId, UniqueNumber(), 2, null, null, null, price, null, null), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<RoomDto>(Ct))!;
    }

    [Fact]
    public async Task Amenities_AreSeeded_AndAssignedToType()
    {
        var owner = await OwnerAsync();
        var amenities = (await owner.GetFromJsonAsync<List<AmenityDto>>("/api/v1/amenities", Ct))!;
        amenities.Select(a => a.Code).Should().Contain(["wifi", "ac", "mountain_view"]);

        var type = await CreateTypeAsync(owner);
        var set = await owner.PutAsJsonAsync($"/api/v1/room-types/{type.Id}/amenities",
            new SetAmenitiesRequest([amenities.Single(a => a.Code == "wifi").Id, amenities.Single(a => a.Code == "ac").Id]), Ct);

        (await set.Content.ReadFromJsonAsync<RoomTypeDto>(Ct))!.Amenities.Select(a => a.Code).Should().BeEquivalentTo(["wifi", "ac"]);
        (await owner.DeleteAsync(new Uri($"/api/v1/amenities/{amenities.Single(a => a.Code == "wifi").Id}", UriKind.Relative), Ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "удобство используется категорией");
    }

    [Fact]
    public async Task Room_InheritsTypeCharacteristics_AndOverridesPrice()
    {
        var owner = await OwnerAsync();
        var type = await CreateTypeAsync(owner, 3500m);

        var plain = await CreateRoomAsync(owner, type.Id);
        var premium = await CreateRoomAsync(owner, type.Id, 4200.50m);

        plain.EffectivePrice.Should().Be(3500m);
        plain.EffectiveCapacity.Should().Be(2);
        plain.EffectiveBedType.Should().Be("Double");
        premium.EffectivePrice.Should().Be(4200.50m);
        plain.Status.Should().Be("Available");
    }

    [Fact]
    public async Task Room_DuplicateNumber_Returns409()
    {
        var owner = await OwnerAsync();
        var type = await CreateTypeAsync(owner);
        var room = await CreateRoomAsync(owner, type.Id);

        var duplicate = await owner.PostAsJsonAsync("/api/v1/rooms",
            new SaveRoomRequest(type.Id, room.Number, 3, null, null, null, null, null, null), Ct);

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemCodeAsync(duplicate)).Should().Be("room.number_taken");
    }

    [Fact]
    public async Task Room_StaleVersion_Returns409()
    {
        var owner = await OwnerAsync();
        var type = await CreateTypeAsync(owner);
        var room = await CreateRoomAsync(owner, type.Id);

        var first = await owner.PutAsJsonAsync($"/api/v1/rooms/{room.Id}",
            new SaveRoomRequest(type.Id, room.Number, 5, null, null, null, null, "Ремонт 2026", room.Version), Ct);
        var stale = await owner.PutAsJsonAsync($"/api/v1/rooms/{room.Id}",
            new SaveRoomRequest(type.Id, room.Number, 6, null, null, null, null, null, room.Version), Ct);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task StatusChange_FollowsStateMachine_AndRecordsHistoryWithAuthor()
    {
        var owner = await OwnerAsync();
        var type = await CreateTypeAsync(owner);
        var room = await CreateRoomAsync(owner, type.Id);
        room.ManualStatusTargets.Should().BeEquivalentTo(["Maintenance", "OutOfService"]);

        var noReason = await owner.PostAsJsonAsync($"/api/v1/rooms/{room.Id}/status", new ChangeRoomStatusRequest("Maintenance", ""), Ct);
        noReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var occupied = await owner.PostAsJsonAsync($"/api/v1/rooms/{room.Id}/status", new ChangeRoomStatusRequest("Occupied", "вручную"), Ct);
        occupied.StatusCode.Should().Be(HttpStatusCode.Conflict, "занятость меняет только заселение");
        (await ProblemCodeAsync(occupied)).Should().Be("room.invalid_status_transition");

        var maintenance = await owner.PostAsJsonAsync($"/api/v1/rooms/{room.Id}/status", new ChangeRoomStatusRequest("Maintenance", "Течёт кран"), Ct);
        maintenance.StatusCode.Should().Be(HttpStatusCode.OK);
        (await maintenance.Content.ReadFromJsonAsync<RoomDto>(Ct))!.Status.Should().Be("Maintenance");

        var history = (await owner.GetFromJsonAsync<List<RoomStatusHistoryDto>>($"/api/v1/rooms/{room.Id}/status-history", Ct))!;
        history.Should().ContainSingle();
        history[0].Should().BeEquivalentTo(new { FromStatus = "Available", ToStatus = "Maintenance", Source = "Manual", Reason = "Течёт кран", ChangedByName = "Тестовый владелец" });
    }

    [Fact]
    public async Task Room_WithHistory_CannotBeDeleted_OnlyArchived()
    {
        var owner = await OwnerAsync();
        var type = await CreateTypeAsync(owner);
        var fresh = await CreateRoomAsync(owner, type.Id);
        var used = await CreateRoomAsync(owner, type.Id);
        await owner.PostAsJsonAsync($"/api/v1/rooms/{used.Id}/status", new ChangeRoomStatusRequest("OutOfService", "Списан"), Ct);

        (await owner.DeleteAsync(new Uri($"/api/v1/rooms/{fresh.Id}", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var delete = await owner.DeleteAsync(new Uri($"/api/v1/rooms/{used.Id}", UriKind.Relative), Ct);
        delete.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemCodeAsync(delete)).Should().Be("room.has_history");

        (await owner.PostAsync(new Uri($"/api/v1/rooms/{used.Id}/archive", UriKind.Relative), null, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        var board = (await owner.GetFromJsonAsync<List<RoomDto>>("/api/v1/rooms/board", Ct))!;
        board.Should().NotContain(r => r.Id == used.Id);
    }

    [Fact]
    public async Task RoomType_WithActiveRooms_CannotBeArchived_AndArchivedTypeRejectsNewRooms()
    {
        var owner = await OwnerAsync();
        var type = await CreateTypeAsync(owner);
        var room = await CreateRoomAsync(owner, type.Id);

        var blocked = await owner.PostAsync(new Uri($"/api/v1/room-types/{type.Id}/archive", UriKind.Relative), null, Ct);
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await owner.PostAsync(new Uri($"/api/v1/rooms/{room.Id}/archive", UriKind.Relative), null, Ct);
        (await owner.PostAsync(new Uri($"/api/v1/room-types/{type.Id}/archive", UriKind.Relative), null, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var create = await owner.PostAsJsonAsync("/api/v1/rooms", new SaveRoomRequest(type.Id, UniqueNumber(), 1, null, null, null, null, null, null), Ct);
        create.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ProblemCodeAsync(create)).Should().Be("room_type.archived");
    }

    [Fact]
    public async Task Photos_UploadServeReorderDelete()
    {
        var owner = await OwnerAsync();
        var type = await CreateTypeAsync(owner);

        var first = await UploadAsync(owner, $"/api/v1/room-types/{type.Id}/photos", TinyPng, "room.png");
        var second = await UploadAsync(owner, $"/api/v1/room-types/{type.Id}/photos", TinyPng, "room2.png");
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var photo1 = (await first.Content.ReadFromJsonAsync<PhotoDto>(Ct))!;
        var photo2 = (await second.Content.ReadFromJsonAsync<PhotoDto>(Ct))!;

        // Публичная отдача без входа, с безопасными заголовками
        var served = await _api.Anonymous().GetAsync(new Uri(photo1.Url, UriKind.Relative), Ct);
        served.StatusCode.Should().Be(HttpStatusCode.OK);
        served.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        served.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        (await served.Content.ReadAsByteArrayAsync(Ct)).Should().Equal(TinyPng);

        var reordered = await owner.PutAsJsonAsync($"/api/v1/room-types/{type.Id}/photos/order", new ReorderPhotosRequest([photo2.Id, photo1.Id]), Ct);
        (await reordered.Content.ReadFromJsonAsync<RoomTypeDto>(Ct))!.Photos.Select(p => p.Id).Should().Equal(photo2.Id, photo1.Id);

        (await owner.DeleteAsync(new Uri($"/api/v1/room-types/{type.Id}/photos/{photo1.Id}", UriKind.Relative), Ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _api.Anonymous().GetAsync(new Uri(photo1.Url, UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("fake.png", "это не картинка")]
    [InlineData("script.jpg", "<script>alert(1)</script>")]
    public async Task Photos_NonImageContent_IsRejected_RegardlessOfExtension(string fileName, string content)
    {
        var owner = await OwnerAsync();
        var type = await CreateTypeAsync(owner);

        var response = await UploadAsync(owner, $"/api/v1/room-types/{type.Id}/photos", System.Text.Encoding.UTF8.GetBytes(content), fileName);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(response)).Should().Be("file.unsupported_type");
    }

    [Fact]
    public async Task PublicCatalog_ShowsSellableTypes_WithLowestPrice()
    {
        var owner = await OwnerAsync();
        var sellable = await CreateTypeAsync(owner, 5000m);
        await CreateRoomAsync(owner, sellable.Id);
        await CreateRoomAsync(owner, sellable.Id, 4500m);
        var empty = await CreateTypeAsync(owner, 1000m);

        var catalog = (await _api.Anonymous().GetFromJsonAsync<List<PublicRoomTypeDto>>("/api/v1/public/room-types", Ct))!;

        var type = catalog.Should().ContainSingle(t => t.Id == sellable.Id).Which;
        type.PriceFrom.Should().Be(4500m);
        type.Rooms.Should().HaveCount(2, "гость выбирает конкретный номер — каталог показывает номера в продаже");
        catalog.Should().NotContain(t => t.Id == empty.Id, "категория без номеров в продаже не показывается");
        (await _api.Anonymous().GetAsync(new Uri($"/api/v1/public/room-types/{empty.Id}", UriKind.Relative), Ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Permissions_AdministratorManages_HousekeeperAndGuestDenied()
    {
        var (_, admin) = await _api.CreateActiveStaffAsync(Roles.Administrator);
        var (_, housekeeper) = await _api.CreateActiveStaffAsync(Roles.Housekeeper);
        var (guestEmail, _) = await _api.RegisterConfirmedGuestAsync();
        var guest = await _api.LoginAsync(guestEmail, GuestPassword);

        var type = await CreateTypeAsync(_api.Authorized(admin.AccessToken));
        await CreateRoomAsync(_api.Authorized(admin.AccessToken), type.Id);

        foreach (var token in new[] { housekeeper.AccessToken, guest.AccessToken })
        {
            var client = _api.Authorized(token);
            (await client.GetAsync(new Uri("/api/v1/rooms", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await client.PostAsJsonAsync("/api/v1/room-types", new SaveRoomTypeRequest("X", null, 1, 1, "Single", 100m, 0, null), Ct))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task Validation_RejectsBadPricesAndNumbers()
    {
        var owner = await OwnerAsync();

        var badType = await owner.PostAsJsonAsync("/api/v1/room-types",
            new SaveRoomTypeRequest("Х", null, 0, 1, "Hammock", 10.005m, 0, null), Ct);
        badType.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await badType.Content.ReadAsStringAsync(Ct)).Should().Contain("capacity").And.Contain("bedType").And.Contain("basePrice");

        var type = await CreateTypeAsync(owner);
        var badRoom = await owner.PostAsJsonAsync("/api/v1/rooms",
            new SaveRoomRequest(type.Id, "1 01; DROP", null, null, null, null, -5m, null, null), Ct);
        badRoom.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string url, byte[] bytes, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "file", fileName);
        return await client.PostAsync(new Uri(url, UriKind.Relative), content, Ct);
    }
}
