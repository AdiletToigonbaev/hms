using System.Net;
using System.Net.Http.Json;
using Hms.Modules.Bookings.Application;
using Hms.Modules.Identity.Contracts;
using Hms.Modules.Pricing.Application;
using Hms.Modules.Pricing.Contracts;
using Hms.Modules.Rooms.Application;
using Hms.Platform.Time;
using Hms.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using static Hms.IntegrationTests.TestApi;

namespace Hms.IntegrationTests.Bookings;

public class BookingsTests(PostgresFixture db)
{
    private readonly TestApi _api = new(db);

    private DateOnly Today => db.Factory.Services.GetRequiredService<IHotelClock>().Today;

    private async Task<HttpClient> OwnerAsync() => _api.Authorized((await _api.LoginOwnerAsync()).AccessToken);

    private static string UniqueNumber() => Random.Shared.Next(1000, 999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<(RoomTypeDto Type, RoomDto Room)> CreateRoomAsync(HttpClient client, decimal price = 3000m, int capacity = 2)
    {
        var typeResponse = await client.PostAsJsonAsync("/api/v1/room-types",
            new SaveRoomTypeRequest($"Тест {Guid.NewGuid():N}"[..20], "Номер для тестов брони", capacity, 1, "Double", price, 10, null), Ct);
        typeResponse.StatusCode.Should().Be(HttpStatusCode.Created, await typeResponse.Content.ReadAsStringAsync(Ct));
        var type = (await typeResponse.Content.ReadFromJsonAsync<RoomTypeDto>(Ct))!;
        var roomResponse = await client.PostAsJsonAsync("/api/v1/rooms",
            new SaveRoomRequest(type.Id, UniqueNumber(), 1, null, null, null, null, null, null), Ct);
        roomResponse.StatusCode.Should().Be(HttpStatusCode.Created, await roomResponse.Content.ReadAsStringAsync(Ct));
        return (type, (await roomResponse.Content.ReadFromJsonAsync<RoomDto>(Ct))!);
    }

    private static CreateBookingRequest Request(Guid roomId, DateOnly checkIn, int nights, int adults = 2, string? promo = null, bool confirm = true) =>
        new(roomId, checkIn, checkIn.AddDays(nights), adults, 0, "Асель Тестова", "+996555123456", null, null, promo, null, confirm);

    private static async Task<HttpResponseMessage> PostBookingAsync(HttpClient client, CreateBookingRequest request, string? key = null)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/bookings") { Content = JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N"));
        return await client.SendAsync(message, Ct);
    }

    private static async Task<BookingDto> CreateBookingAsync(HttpClient client, CreateBookingRequest request)
    {
        var response = await PostBookingAsync(client, request);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<BookingDto>(Ct))!;
    }

    [Fact]
    public async Task ParallelOverlappingBookings_ExactlyOneSucceeds()
    {
        var owner = await OwnerAsync();
        var (_, room) = await CreateRoomAsync(owner);
        var start = Today.AddDays(40);

        // Десять параллельных запросов на пересекающиеся даты: [d, d+3), [d+1, d+4), ...
        var responses = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(i => PostBookingAsync(owner, Request(room.Id, start.AddDays(i % 2), 3))));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        foreach (var rejected in responses.Where(r => r.StatusCode != HttpStatusCode.Created))
        {
            rejected.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await ProblemCodeAsync(rejected)).Should().Be("booking.room_unavailable");
        }
    }

    [Fact]
    public async Task AdjacentStays_AreAllowed_AndCancellationFreesRoom()
    {
        var owner = await OwnerAsync();
        var (_, room) = await CreateRoomAsync(owner);
        var day = Today.AddDays(50);

        var first = await CreateBookingAsync(owner, Request(room.Id, day, 2));
        await CreateBookingAsync(owner, Request(room.Id, day.AddDays(2), 2)); // выезд и заезд в один день

        var overlap = await PostBookingAsync(owner, Request(room.Id, day.AddDays(1), 1));
        overlap.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var cancel = await owner.PostAsJsonAsync($"/api/v1/bookings/{first.Id}/cancel", new CancelBookingRequest("Гость передумал"), Ct);
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);
        (await cancel.Content.ReadFromJsonAsync<BookingDto>(Ct))!.Status.Should().Be("Cancelled");

        await CreateBookingAsync(owner, Request(room.Id, day.AddDays(1), 1));
    }

    [Fact]
    public async Task Create_SnapshotsPrice_WithSeasonRateAndPromo_AndWritesHistory()
    {
        var owner = await OwnerAsync();
        var (type, room) = await CreateRoomAsync(owner, 3000m);
        var day = Today.AddDays(60);

        (await owner.PostAsJsonAsync("/api/v1/rates",
            new SaveRateRequest(type.Id, "Сезон", day, day.AddDays(30), 127, 4000m, 1, true), Ct)).StatusCode.Should().Be(HttpStatusCode.Created);
        var code = $"P{Guid.NewGuid():N}"[..12];
        (await owner.PostAsJsonAsync("/api/v1/discounts",
            new SaveDiscountRequest("Тестовая скидка", code, "Percent", 10m, null, null, 2), Ct)).StatusCode.Should().Be(HttpStatusCode.Created);

        var quote = await owner.PostAsJsonAsync("/api/v1/bookings/quote", new QuoteRequest(room.Id, day.AddDays(-1), day.AddDays(2), 2, 0, code.ToLowerInvariant(), null), Ct);
        quote.StatusCode.Should().Be(HttpStatusCode.OK, await quote.Content.ReadAsStringAsync(Ct));
        var price = (await quote.Content.ReadFromJsonAsync<PriceQuote>(Ct))!;
        price.Nights.Select(n => n.Price).Should().Equal(3000m, 4000m, 4000m);
        price.DiscountAmount.Should().Be(1100m);
        price.Total.Should().Be(9900m);

        var booking = await CreateBookingAsync(owner, Request(room.Id, day.AddDays(-1), 3, promo: code));
        booking.TotalAmount.Should().Be(9900m);
        booking.Number.Should().MatchRegex(@"^С-\d{4}-\d{2,}$", "посуточная бронь: «С-ДДММ-NN»");

        // Изменение тарифа после создания не меняет зафиксированную стоимость брони.
        var rates = (await owner.GetFromJsonAsync<List<RateDto>>($"/api/v1/rates?roomTypeId={type.Id}", Ct))!;
        (await owner.DeleteAsync(new Uri($"/api/v1/rates/{rates[0].Id}", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await owner.GetFromJsonAsync<BookingDto>($"/api/v1/bookings/{booking.Id}", Ct))!.TotalAmount.Should().Be(9900m);

        var history = (await owner.GetFromJsonAsync<List<BookingHistoryDto>>($"/api/v1/bookings/{booking.Id}/history", Ct))!;
        history.Should().ContainSingle(h => h.EventType == "created").Which.ChangedByName.Should().Be("Тестовый владелец");
    }

    [Fact]
    public async Task PromoRules_AreEnforced()
    {
        var owner = await OwnerAsync();
        var (_, room) = await CreateRoomAsync(owner);
        var code = $"M{Guid.NewGuid():N}"[..12];
        await owner.PostAsJsonAsync("/api/v1/discounts", new SaveDiscountRequest("От 3 ночей", code, "Amount", 500m, null, null, 3), Ct);

        var shortStay = await PostBookingAsync(owner, Request(room.Id, Today.AddDays(70), 2, promo: code));
        shortStay.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ProblemCodeAsync(shortStay)).Should().Be("pricing.promo_min_nights");

        var unknown = await PostBookingAsync(owner, Request(room.Id, Today.AddDays(70), 3, promo: "NOSUCHCODE"));
        (await ProblemCodeAsync(unknown)).Should().Be("pricing.promo_not_found");

        var duplicate = await owner.PostAsJsonAsync("/api/v1/discounts", new SaveDiscountRequest("Дубль", code.ToLowerInvariant(), "Percent", 5m, null, null, 0), Ct);
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Capacity_PastDates_AndNonSellableRoom_AreRejected()
    {
        var owner = await OwnerAsync();
        var (_, room) = await CreateRoomAsync(owner, capacity: 2);

        var tooMany = await PostBookingAsync(owner, Request(room.Id, Today.AddDays(80), 2, adults: 3));
        (await ProblemCodeAsync(tooMany)).Should().Be("booking.capacity_exceeded");

        var past = await PostBookingAsync(owner, Request(room.Id, Today.AddDays(-1), 2));
        (await ProblemCodeAsync(past)).Should().Be("booking.past_dates");

        await owner.PostAsJsonAsync($"/api/v1/rooms/{room.Id}/status", new ChangeRoomStatusRequest("OutOfService", "Ремонт"), Ct);
        var outOfService = await PostBookingAsync(owner, Request(room.Id, Today.AddDays(80), 2));
        (await ProblemCodeAsync(outOfService)).Should().Be("booking.room_not_sellable");
    }

    [Fact]
    public async Task Idempotency_ReplaysSameResponse_AndRejectsKeyReuse()
    {
        var owner = await OwnerAsync();
        var (_, room) = await CreateRoomAsync(owner);
        var key = Guid.NewGuid().ToString("N");
        var request = Request(room.Id, Today.AddDays(90), 2);

        var first = await PostBookingAsync(owner, request, key);
        var replay = await PostBookingAsync(owner, request, key);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        replay.StatusCode.Should().Be(HttpStatusCode.Created);
        replay.Headers.Contains("Idempotent-Replayed").Should().BeTrue();
        (await replay.Content.ReadFromJsonAsync<BookingDto>(Ct))!.Id.Should().Be((await first.Content.ReadFromJsonAsync<BookingDto>(Ct))!.Id);

        var reused = await PostBookingAsync(owner, request with { Adults = 1 }, key);
        (await ProblemCodeAsync(reused)).Should().Be("idempotency.key_reused");

        using var noKey = new HttpRequestMessage(HttpMethod.Post, "/api/v1/bookings") { Content = JsonContent.Create(request) };
        var missing = await owner.SendAsync(noKey, Ct);
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(missing)).Should().Be("idempotency.key_required");
    }

    [Fact]
    public async Task RoomWithBookings_ShowsReservedToday_AndIsGuardedFromDeleteArchiveOutOfService()
    {
        var owner = await OwnerAsync();
        var (_, room) = await CreateRoomAsync(owner);
        await CreateBookingAsync(owner, Request(room.Id, Today, 1));

        (await owner.GetFromJsonAsync<RoomDto>($"/api/v1/rooms/{room.Id}", Ct))!.DisplayStatus.Should().Be("Reserved");

        var delete = await owner.DeleteAsync(new Uri($"/api/v1/rooms/{room.Id}", UriKind.Relative), Ct);
        (await ProblemCodeAsync(delete)).Should().Be("room.has_bookings");
        var archive = await owner.PostAsync(new Uri($"/api/v1/rooms/{room.Id}/archive", UriKind.Relative), null, Ct);
        (await ProblemCodeAsync(archive)).Should().Be("room.has_upcoming_bookings");
        var oos = await owner.PostAsJsonAsync($"/api/v1/rooms/{room.Id}/status", new ChangeRoomStatusRequest("OutOfService", "Ремонт"), Ct);
        (await ProblemCodeAsync(oos)).Should().Be("room.has_upcoming_bookings");
    }

    [Fact]
    public async Task Availability_ExcludesBookedRooms_PublicHidesRoomNumbers()
    {
        var owner = await OwnerAsync();
        var (type, free) = await CreateRoomAsync(owner, 2500m);
        var busyResponse = await owner.PostAsJsonAsync("/api/v1/rooms", new SaveRoomRequest(type.Id, UniqueNumber(), 1, null, null, null, null, null, null), Ct);
        var busy = (await busyResponse.Content.ReadFromJsonAsync<RoomDto>(Ct))!;
        var day = Today.AddDays(100);
        await CreateBookingAsync(owner, Request(busy.Id, day, 2));

        var rooms = (await owner.GetFromJsonAsync<List<AvailableRoomDto>>(
            $"/api/v1/availability?checkIn={day:yyyy-MM-dd}&checkOut={day.AddDays(2):yyyy-MM-dd}&adults=2&roomTypeId={type.Id}", Ct))!;
        rooms.Select(r => r.RoomId).Should().Equal(free.Id);
        rooms[0].Quote.Total.Should().Be(5000m);

        var publicJson = await _api.Anonymous().GetStringAsync(
            $"/api/v1/public/availability?checkIn={day:yyyy-MM-dd}&checkOut={day.AddDays(2):yyyy-MM-dd}&adults=2&roomTypeId={type.Id}", Ct);
        publicJson.Should().NotContain(free.Number).And.NotContain(free.Id.ToString());
        var types = System.Text.Json.JsonSerializer.Deserialize<List<PublicAvailabilityDto>>(publicJson, System.Text.Json.JsonSerializerOptions.Web)!;
        types.Should().ContainSingle().Which.Should().BeEquivalentTo(new { RoomTypeId = type.Id, AvailableCount = 1, Nights = 2, TotalFrom = 5000m });

        var invalid = await _api.Anonymous().GetAsync(new Uri($"/api/v1/public/availability?checkIn={day:yyyy-MM-dd}&checkOut={day:yyyy-MM-dd}", UriKind.Relative), Ct);
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Permissions_AdminBooks_HousekeeperAndGuestDenied_AdminCannotManageRates()
    {
        var owner = await OwnerAsync();
        var (type, room) = await CreateRoomAsync(owner);
        var (_, adminTokens) = await _api.CreateActiveStaffAsync(Roles.Administrator);
        var (_, maidTokens) = await _api.CreateActiveStaffAsync(Roles.Housekeeper);
        var (guestEmail, _) = await _api.RegisterConfirmedGuestAsync();
        var admin = _api.Authorized(adminTokens.AccessToken);
        var maid = _api.Authorized(maidTokens.AccessToken);
        var guest = _api.Authorized((await _api.LoginAsync(guestEmail, GuestPassword)).AccessToken);

        var booking = await CreateBookingAsync(admin, Request(room.Id, Today.AddDays(110), 1, confirm: false));
        booking.Status.Should().Be("Pending");
        booking.AllowedActions.Should().BeEquivalentTo(["confirm", "cancel", "reschedule"]);
        (await admin.PostAsync(new Uri($"/api/v1/bookings/{booking.Id}/confirm", UriKind.Relative), null, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await admin.PostAsJsonAsync("/api/v1/rates", new SaveRateRequest(type.Id, "X", Today, Today, 127, 1m, 0, true), Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.GetAsync(new Uri("/api/v1/rates", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        foreach (var client in new[] { maid, guest })
        {
            (await client.GetAsync(new Uri("/api/v1/bookings", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await client.GetAsync(new Uri($"/api/v1/bookings/{booking.Id}", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await PostBookingAsync(client, Request(room.Id, Today.AddDays(120), 1))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task List_FiltersBySearchAndStatus()
    {
        var owner = await OwnerAsync();
        var (_, room) = await CreateRoomAsync(owner);
        var booking = await CreateBookingAsync(owner, Request(room.Id, Today.AddDays(130), 2, confirm: false));

        var page = (await owner.GetFromJsonAsync<PagedResult<BookingListItemDto>>($"/api/v1/bookings?search={booking.Number}&status=Pending", Ct))!;
        page.Items.Should().ContainSingle().Which.Should().BeEquivalentTo(new { booking.Id, RoomNumber = room.Number, Nights = 2 });
        (await owner.GetFromJsonAsync<PagedResult<BookingListItemDto>>($"/api/v1/bookings?search={booking.Number}&status=Cancelled", Ct))!
            .Items.Should().BeEmpty();
    }
}
