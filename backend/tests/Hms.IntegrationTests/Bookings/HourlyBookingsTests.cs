using System.Net;
using System.Net.Http.Json;
using Hms.Modules.Bookings.Application;
using Hms.Modules.Identity.Contracts;
using Hms.Modules.Pricing.Application;
using Hms.Modules.Rooms.Application;
using Hms.Platform.Time;
using Microsoft.Extensions.DependencyInjection;
using static Hms.IntegrationTests.TestApi;

namespace Hms.IntegrationTests.Bookings;

/// <summary>Почасовые брони, уборка между гостями, смешанные брони и продление (решения заказчика 2026-11-20).</summary>
public class HourlyBookingsTests(PostgresFixture db)
{
    private readonly TestApi _api = new(db);

    private DateOnly Today => db.Factory.Services.GetRequiredService<IHotelClock>().Today;

    private async Task<HttpClient> OwnerAsync() => _api.Authorized((await _api.LoginOwnerAsync()).AccessToken);

    private static string UniqueNumber() => Random.Shared.Next(1000, 999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<(RoomTypeDto Type, RoomDto Room)> CreateRoomAsync(HttpClient client, decimal? hourly = 500m)
    {
        var typeResponse = await client.PostAsJsonAsync("/api/v1/room-types",
            new SaveRoomTypeRequest($"Час {Guid.NewGuid():N}"[..20], null, 2, 1, "Double", 3000m, 10, null, HourlyPrice: hourly), Ct);
        typeResponse.StatusCode.Should().Be(HttpStatusCode.Created, await typeResponse.Content.ReadAsStringAsync(Ct));
        var type = (await typeResponse.Content.ReadFromJsonAsync<RoomTypeDto>(Ct))!;
        type.HourlyPrice.Should().Be(hourly);
        var roomResponse = await client.PostAsJsonAsync("/api/v1/rooms",
            new SaveRoomRequest(type.Id, UniqueNumber(), 1, null, null, null, null, null, null), Ct);
        roomResponse.StatusCode.Should().Be(HttpStatusCode.Created, await roomResponse.Content.ReadAsStringAsync(Ct));
        return (type, (await roomResponse.Content.ReadFromJsonAsync<RoomDto>(Ct))!);
    }

    private static CreateBookingRequest Hourly(Guid roomId, DateOnly date, int startHour, int hours, bool confirm = true) =>
        new(roomId, null, null, 2, 0, "Почасовой Гость", "+996555123456", null, null, null, null, confirm,
            Kind: StayKinds.Hourly, Date: date, StartHour: startHour, Hours: hours);

    private static CreateBookingRequest Nightly(Guid roomId, DateOnly checkIn, int nights) =>
        new(roomId, checkIn, checkIn.AddDays(nights), 2, 0, "Посуточный Гость", "+996555123456", null, null, null, null, true);

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, CreateBookingRequest request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/bookings") { Content = JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return await client.SendAsync(message, Ct);
    }

    private static async Task<BookingDto> CreateAsync(HttpClient client, CreateBookingRequest request)
    {
        var response = await PostAsync(client, request);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<BookingDto>(Ct))!;
    }

    [Fact]
    public async Task HourlyBooking_PricedPerHour_WithLocalTimes()
    {
        var owner = await OwnerAsync();
        var (_, room) = await CreateRoomAsync(owner, 500m);
        var day = Today.AddDays(30);

        var booking = await CreateAsync(owner, Hourly(room.Id, day, 10, 3));

        booking.Kind.Should().Be("Hourly");
        booking.Hours.Should().Be(3);
        booking.StartsAt.Should().Be($"{day:yyyy-MM-dd}T10:00");
        booking.EndsAt.Should().Be($"{day:yyyy-MM-dd}T13:00");
        booking.TotalAmount.Should().Be(1500m);
        booking.Price!.Hours.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Hours = 3, UnitPrice = 500m });
        booking.AllowedActions.Should().Contain("extend");
    }

    [Fact]
    public async Task CleaningBuffer_BlocksNextHour_ButAllowsAfterCleaning()
    {
        var owner = await OwnerAsync();
        var (_, room) = await CreateRoomAsync(owner);
        var day = Today.AddDays(31);
        await CreateAsync(owner, Hourly(room.Id, day, 10, 2)); // 10:00–12:00, уборка до 12:30

        var tooEarly = await PostAsync(owner, Hourly(room.Id, day, 12, 2));
        tooEarly.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemCodeAsync(tooEarly)).Should().Be("booking.room_unavailable");

        await CreateAsync(owner, Hourly(room.Id, day, 13, 2));
        // Перед первой бронью: 07:00–09:00 + уборка до 09:30 — не мешает 10:00
        await CreateAsync(owner, Hourly(room.Id, day, 7, 2));
    }

    [Fact]
    public async Task NightlyAndHourly_ShareTheRoom_ByTime()
    {
        var owner = await OwnerAsync();
        var (_, room) = await CreateRoomAsync(owner);
        var day = Today.AddDays(32);
        await CreateAsync(owner, Nightly(room.Id, day, 1)); // заезд 14:00, выезд завтра 12:00 (+30 мин уборки)

        (await ProblemCodeAsync(await PostAsync(owner, Hourly(room.Id, day.AddDays(1), 11, 2)))).Should().Be("booking.room_unavailable");
        (await ProblemCodeAsync(await PostAsync(owner, Hourly(room.Id, day, 13, 2)))).Should().Be("booking.room_unavailable");
        await CreateAsync(owner, Hourly(room.Id, day.AddDays(1), 13, 3));
        await CreateAsync(owner, Hourly(room.Id, day, 10, 3)); // 10:00–13:00 + уборка до 13:30 < заезд 14:00
    }

    [Fact]
    public async Task ParallelOverlappingHourlyBookings_ExactlyOneSucceeds()
    {
        var owner = await OwnerAsync();
        var (_, room) = await CreateRoomAsync(owner);
        var day = Today.AddDays(33);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => PostAsync(owner, Hourly(room.Id, day, 10 + (i % 2), 2))));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Where(r => r.StatusCode != HttpStatusCode.Created).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task HourlyRules_MinMaxHours_PastStart_AndRoomWithoutHourlyPrice()
    {
        var owner = await OwnerAsync();
        var (_, room) = await CreateRoomAsync(owner);
        var (_, nightlyOnly) = await CreateRoomAsync(owner, hourly: null);
        var day = Today.AddDays(34);

        (await ProblemCodeAsync(await PostAsync(owner, Hourly(room.Id, day, 10, 1)))).Should().Be("booking.hours_out_of_range");
        (await ProblemCodeAsync(await PostAsync(owner, Hourly(room.Id, day, 10, 13)))).Should().Be("booking.hours_out_of_range");
        (await ProblemCodeAsync(await PostAsync(owner, Hourly(room.Id, Today.AddDays(-1), 10, 2)))).Should().Be("booking.start_in_past");
        (await ProblemCodeAsync(await PostAsync(owner, Hourly(nightlyOnly.Id, day, 10, 2)))).Should().Be("booking.room_not_hourly");

        var invalid = await PostAsync(owner, Hourly(room.Id, day, 10, 2) with { StartHour = 25 });
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task HourRates_ApplyOnlyToHourlyBookings()
    {
        var owner = await OwnerAsync();
        var (type, room) = await CreateRoomAsync(owner, 500m);
        var day = Today.AddDays(35);
        (await owner.PostAsJsonAsync("/api/v1/rates",
            new SaveRateRequest(type.Id, "Часы выходного", day, day, 127, 800m, 1, true, Unit: "Hour"), Ct)).StatusCode.Should().Be(HttpStatusCode.Created);

        var hourly = await owner.PostAsJsonAsync("/api/v1/bookings/quote",
            new QuoteRequest(room.Id, null, null, 2, 0, null, null, Kind: StayKinds.Hourly, Date: day, StartHour: 22, Hours: 4), Ct);
        var quote = (await hourly.Content.ReadFromJsonAsync<Modules.Pricing.Contracts.PriceQuote>(Ct))!;
        quote.Total.Should().Be(2 * 800m + 2 * 500m, "22:00 и 23:00 — по тарифу дня, 00:00 и 01:00 следующего дня — цена часа");

        var nightly = await owner.PostAsJsonAsync("/api/v1/bookings/quote", new QuoteRequest(room.Id, day, day.AddDays(1), 2, 0, null, null), Ct);
        (await nightly.Content.ReadFromJsonAsync<Modules.Pricing.Contracts.PriceQuote>(Ct))!.Total.Should().Be(3000m, "часовой тариф не меняет цену ночи");
    }

    [Fact]
    public async Task Extend_Hourly_UntilNextBooking_WithQuoteAndHistory()
    {
        var owner = await OwnerAsync();
        var (_, room) = await CreateRoomAsync(owner, 500m);
        var day = Today.AddDays(36);
        var booking = await CreateAsync(owner, Hourly(room.Id, day, 10, 2)); // 10–12
        await CreateAsync(owner, Hourly(room.Id, day, 16, 2)); // следующий гость с 16:00 → продлить можно до 15:30

        var quote = (await owner.GetFromJsonAsync<ExtensionQuoteDto>($"/api/v1/bookings/{booking.Id}/extend/quote?units=2", Ct))!;
        quote.Should().BeEquivalentTo(new { NewEndsAt = $"{day:yyyy-MM-dd}T14:00", Amount = 1000m, MaxUnits = 3, NextBookingStartsAt = $"{day:yyyy-MM-dd}T16:00" });

        var tooLong = await owner.PostAsJsonAsync($"/api/v1/bookings/{booking.Id}/extend", new ExtendBookingRequest(4, booking.Version), Ct);
        tooLong.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemCodeAsync(tooLong)).Should().Be("booking.extension_conflict");
        (await tooLong.Content.ReadAsStringAsync(Ct)).Should().Contain("15:30");

        var extended = await owner.PostAsJsonAsync($"/api/v1/bookings/{booking.Id}/extend", new ExtendBookingRequest(2, booking.Version), Ct);
        extended.StatusCode.Should().Be(HttpStatusCode.OK, await extended.Content.ReadAsStringAsync(Ct));
        var dto = (await extended.Content.ReadFromJsonAsync<BookingDto>(Ct))!;
        dto.Should().BeEquivalentTo(new { Hours = 4, EndsAt = $"{day:yyyy-MM-dd}T14:00", TotalAmount = 2000m, ExtensionsAmount = 1000m });
        dto.Extensions.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Quantity = 2, Amount = 1000m, CreatedByName = "Тестовый владелец" });

        // Повтор со старой версией (двойное нажатие) не продлевает второй раз
        var replay = await owner.PostAsJsonAsync($"/api/v1/bookings/{booking.Id}/extend", new ExtendBookingRequest(1, booking.Version), Ct);
        (await ProblemCodeAsync(replay)).Should().Be("concurrency.conflict");

        // Номер занят до 14:30 — бронь с 14:00 невозможна
        (await ProblemCodeAsync(await PostAsync(owner, Hourly(room.Id, day, 14, 2)))).Should().Be("booking.room_unavailable");
        var history = (await owner.GetFromJsonAsync<List<BookingHistoryDto>>($"/api/v1/bookings/{booking.Id}/history", Ct))!;
        history.Should().Contain(h => h.EventType == "extended");
    }

    [Fact]
    public async Task Extend_Nightly_AddsNightAtCurrentPrice()
    {
        var owner = await OwnerAsync();
        var (_, room) = await CreateRoomAsync(owner);
        var day = Today.AddDays(40);
        var booking = await CreateAsync(owner, Nightly(room.Id, day, 2));

        var response = await owner.PostAsJsonAsync($"/api/v1/bookings/{booking.Id}/extend", new ExtendBookingRequest(1, booking.Version), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var dto = (await response.Content.ReadFromJsonAsync<BookingDto>(Ct))!;
        dto.Should().BeEquivalentTo(new { CheckOut = day.AddDays(3), Nights = 3, TotalAmount = 9000m, ExtensionsAmount = 3000m });
    }

    [Fact]
    public async Task Extend_RespectsStatusMaxHoursAndPermissions()
    {
        var owner = await OwnerAsync();
        var (_, room) = await CreateRoomAsync(owner);
        var day = Today.AddDays(41);
        var pending = await CreateAsync(owner, Hourly(room.Id, day, 8, 2, confirm: false));
        var confirmed = await CreateAsync(owner, Hourly(room.Id, day, 14, 10));

        (await ProblemCodeAsync(await owner.PostAsJsonAsync($"/api/v1/bookings/{pending.Id}/extend", new ExtendBookingRequest(1, null), Ct)))
            .Should().Be("booking.extend_not_allowed");
        (await ProblemCodeAsync(await owner.PostAsJsonAsync($"/api/v1/bookings/{confirmed.Id}/extend", new ExtendBookingRequest(3, null), Ct)))
            .Should().Be("booking.extension_too_long");

        var (_, maidTokens) = await _api.CreateActiveStaffAsync(Roles.Housekeeper);
        var maid = _api.Authorized(maidTokens.AccessToken);
        (await maid.PostAsJsonAsync($"/api/v1/bookings/{confirmed.Id}/extend", new ExtendBookingRequest(1, null), Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await maid.GetAsync(new Uri($"/api/v1/bookings/{confirmed.Id}/extend/quote?units=1", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RoomBoard_ShowsNextBookingToday_AndHourlyPrice()
    {
        var clock = db.Factory.Services.GetRequiredService<IHotelClock>();
        var nextHour = clock.LocalNow.Hour + 1;
        if (nextHour > 23)
        {
            return; // в 23:xx по Бишкеку сегодня позже начать нельзя — проверка не имеет смысла
        }

        var owner = await OwnerAsync();
        var (_, room) = await CreateRoomAsync(owner, 450m);
        await CreateAsync(owner, Hourly(room.Id, Today, nextHour, 2));

        var dto = (await owner.GetFromJsonAsync<RoomDto>($"/api/v1/rooms/{room.Id}", Ct))!;

        dto.DisplayStatus.Should().Be("Reserved");
        dto.NextBookingAt.Should().Be($"{Today:yyyy-MM-dd}T{nextHour:D2}:00");
        dto.OccupiedUntil.Should().BeNull();
        dto.EffectiveHourlyPrice.Should().Be(450m);
    }

    [Fact]
    public async Task Promo_MinHours_AppliesToHourly_MinNightsOnlyToNightly()
    {
        var owner = await OwnerAsync();
        var (_, room) = await CreateRoomAsync(owner, 500m);
        var code = $"H{Guid.NewGuid():N}"[..12];
        (await owner.PostAsJsonAsync("/api/v1/discounts",
            new SaveDiscountRequest("От 3 часов", code, "Percent", 10m, null, null, 2, MinHours: 3), Ct)).StatusCode.Should().Be(HttpStatusCode.Created);
        var day = Today.AddDays(43);

        var tooShort = await owner.PostAsJsonAsync("/api/v1/bookings/quote",
            new QuoteRequest(room.Id, null, null, 2, 0, code, null, Kind: StayKinds.Hourly, Date: day, StartHour: 10, Hours: 2), Ct);
        (await ProblemCodeAsync(tooShort)).Should().Be("pricing.promo_min_hours");

        var ok = await owner.PostAsJsonAsync("/api/v1/bookings/quote",
            new QuoteRequest(room.Id, null, null, 2, 0, code, null, Kind: StayKinds.Hourly, Date: day, StartHour: 10, Hours: 3), Ct);
        (await ok.Content.ReadFromJsonAsync<Modules.Pricing.Contracts.PriceQuote>(Ct))!.Total.Should().Be(1350m, "3 × 500 − 10 %; минимум ночей к часам не относится");
    }

    [Fact]
    public async Task HourlyAvailability_ShowsOnlyHourlyRoomsFreeAtThatTime()
    {
        var owner = await OwnerAsync();
        var (type, free) = await CreateRoomAsync(owner, 600m);
        var busyResponse = await owner.PostAsJsonAsync("/api/v1/rooms", new SaveRoomRequest(type.Id, UniqueNumber(), 1, null, null, null, null, null, null), Ct);
        var busy = (await busyResponse.Content.ReadFromJsonAsync<RoomDto>(Ct))!;
        var day = Today.AddDays(42);
        await CreateAsync(owner, Hourly(busy.Id, day, 11, 2));

        var rooms = (await owner.GetFromJsonAsync<List<AvailableRoomDto>>(
            $"/api/v1/availability?kind=Hourly&date={day:yyyy-MM-dd}&startHour=12&hours=3&adults=2&roomTypeId={type.Id}", Ct))!;

        rooms.Select(r => r.RoomId).Should().Equal(free.Id);
        rooms[0].Quote.Total.Should().Be(1800m);

        var publicTypes = (await _api.Anonymous().GetFromJsonAsync<List<PublicAvailabilityDto>>(
            $"/api/v1/public/availability?kind=Hourly&date={day:yyyy-MM-dd}&startHour=12&hours=3&adults=2&roomTypeId={type.Id}", Ct))!;
        publicTypes.Should().ContainSingle().Which.Should().BeEquivalentTo(new { AvailableCount = 1, Kind = "Hourly", Units = 3, TotalFrom = 1800m });
    }
}
