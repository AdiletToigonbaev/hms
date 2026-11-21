using System.Net;
using System.Net.Http.Json;
using Hms.Modules.Bookings.Application;
using Hms.Modules.Hotel.Application;
using Hms.Modules.Rooms.Application;
using Hms.Platform.Time;
using Microsoft.Extensions.DependencyInjection;
using static Hms.IntegrationTests.TestApi;

namespace Hms.IntegrationTests.Bookings;

/// <summary>Время уборки: общее (после почасовой и после посуточной) и своё у категории номера.</summary>
public class CleaningTests(PostgresFixture db)
{
    private readonly TestApi _api = new(db);

    private DateOnly Today => db.Factory.Services.GetRequiredService<IHotelClock>().Today;

    private async Task<HttpClient> OwnerAsync() => _api.Authorized((await _api.LoginOwnerAsync()).AccessToken);

    private static async Task<RoomDto> RoomAsync(HttpClient client, int? hourlyCleaning, int? nightlyCleaning)
    {
        var type = await client.PostAsJsonAsync("/api/v1/room-types",
            new SaveRoomTypeRequest($"Уб {Guid.NewGuid():N}"[..20], null, 2, 1, "Double", 3000m, 10, null, HourlyPrice: 500m,
                CleaningHourlyMinutes: hourlyCleaning, CleaningNightlyMinutes: nightlyCleaning), Ct);
        type.StatusCode.Should().Be(HttpStatusCode.Created, await type.Content.ReadAsStringAsync(Ct));
        var typeDto = (await type.Content.ReadFromJsonAsync<RoomTypeDto>(Ct))!;
        typeDto.CleaningHourlyMinutes.Should().Be(hourlyCleaning);
        typeDto.CleaningNightlyMinutes.Should().Be(nightlyCleaning);
        var room = await client.PostAsJsonAsync("/api/v1/rooms",
            new SaveRoomRequest(typeDto.Id, BookingTestKit.UniqueNumber(), 1, null, null, null, null, null, null), Ct);
        room.StatusCode.Should().Be(HttpStatusCode.Created, await room.Content.ReadAsStringAsync(Ct));
        return (await room.Content.ReadFromJsonAsync<RoomDto>(Ct))!;
    }

    private static async Task<CalendarBookingDto> BlockAsync(HttpClient client, Guid bookingId, DateOnly day) =>
        (await client.GetFromJsonAsync<CalendarDto>($"/api/v1/calendar?from={day:yyyy-MM-dd}&to={day.AddDays(3):yyyy-MM-dd}", Ct))!
            .Bookings.Single(b => b.Id == bookingId);

    [Fact]
    public async Task Defaults_AreThirtyMinutes_ForHourlyAndNightly()
    {
        var owner = await OwnerAsync();
        var settings = (await owner.GetFromJsonAsync<HotelSettingsDto>("/api/v1/hotel/settings", Ct))!;

        settings.CleaningBufferMinutes.Should().Be(30);
        settings.CleaningBufferNightlyMinutes.Should().Be(30);
    }

    [Fact]
    public async Task CategoryHourlyCleaning_OverridesHotelDefault()
    {
        var owner = await OwnerAsync();
        var room = await RoomAsync(owner, hourlyCleaning: 60, nightlyCleaning: null);
        var day = Today.AddDays(45);
        var first = await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(room.Id, day, 10, 2));

        (await BlockAsync(owner, first.Id, day)).BlockedUntil.Should().Be($"{day:yyyy-MM-dd}T13:00", "уборка категории — 60 минут");
        var tooEarly = await BookingTestKit.PostAsync(owner, BookingTestKit.Hourly(room.Id, day, 12, 2));
        tooEarly.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(room.Id, day, 13, 2));
    }

    [Fact]
    public async Task CategoryNightlyCleaning_IsCappedByGapBetweenCheckOutAndCheckIn()
    {
        var owner = await OwnerAsync();
        var day = Today.AddDays(46);
        var ninety = await RoomAsync(owner, hourlyCleaning: null, nightlyCleaning: 90);
        var long_ = await RoomAsync(owner, hourlyCleaning: null, nightlyCleaning: 200);

        var a = await BookingTestKit.CreateAsync(owner, new CreateBookingRequest(ninety.Id, day, day.AddDays(1), 2, 0, "Гость", "+996555123456", null, null, null, null, true));
        var b = await BookingTestKit.CreateAsync(owner, new CreateBookingRequest(long_.Id, day, day.AddDays(1), 2, 0, "Гость", "+996555123456", null, null, null, null, true));

        (await BlockAsync(owner, a.Id, day)).BlockedUntil.Should().Be($"{day.AddDays(1):yyyy-MM-dd}T13:30");
        (await BlockAsync(owner, b.Id, day)).BlockedUntil.Should().Be($"{day.AddDays(1):yyyy-MM-dd}T14:00", "не дольше промежутка между выездом 12:00 и заездом 14:00");
    }

    [Fact]
    public async Task Availability_UsesEachRoomsOwnCleaning()
    {
        var owner = await OwnerAsync();
        var day = Today.AddDays(47);
        var quick = await RoomAsync(owner, hourlyCleaning: null, nightlyCleaning: null);
        var slow = await RoomAsync(owner, hourlyCleaning: 90, nightlyCleaning: null);
        await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(quick.Id, day, 9, 2));
        await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(slow.Id, day, 9, 2));

        // Новая бронь с 12:00: после брони 09–11 у «быстрого» номера уборка до 11:30, у «медленного» — до 12:30.
        var free = (await owner.GetFromJsonAsync<List<AvailableRoomDto>>(
            $"/api/v1/availability?kind=Hourly&date={day:yyyy-MM-dd}&startHour=11&hours=2&adults=1", Ct))!;
        free.Should().NotContain(r => r.RoomId == quick.Id || r.RoomId == slow.Id, "в 11:00 оба ещё на уборке");
        free = (await owner.GetFromJsonAsync<List<AvailableRoomDto>>(
            $"/api/v1/availability?kind=Hourly&date={day:yyyy-MM-dd}&startHour=12&hours=2&adults=1", Ct))!;
        free.Should().Contain(r => r.RoomId == quick.Id).And.NotContain(r => r.RoomId == slow.Id);
    }
}
