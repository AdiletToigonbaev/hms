using System.Net;
using System.Net.Http.Json;
using Hms.IntegrationTests.Hotel;
using Hms.Modules.Bookings.Application;
using Hms.Modules.Hotel.Application;
using Hms.Modules.Identity.Contracts;
using Hms.Modules.Rooms.Application;
using Hms.Platform.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Hms.IntegrationTests.TestApi;

namespace Hms.IntegrationTests.Bookings;

/// <summary>Общие помощники для тестов Sprint 5: номер с ценой часа, почасовые брони.</summary>
internal static class BookingTestKit
{
    public static string UniqueNumber() => Random.Shared.Next(1000, 999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static async Task<RoomDto> CreateRoomAsync(HttpClient client, decimal hourly = 500m, int capacity = 2)
    {
        var typeResponse = await client.PostAsJsonAsync("/api/v1/room-types",
            new SaveRoomTypeRequest($"Кал {Guid.NewGuid():N}"[..20], null, capacity, 1, "Double", 3000m, 10, null, HourlyPrice: hourly), Ct);
        typeResponse.StatusCode.Should().Be(HttpStatusCode.Created, await typeResponse.Content.ReadAsStringAsync(Ct));
        var type = (await typeResponse.Content.ReadFromJsonAsync<RoomTypeDto>(Ct))!;
        var roomResponse = await client.PostAsJsonAsync("/api/v1/rooms",
            new SaveRoomRequest(type.Id, UniqueNumber(), 1, null, null, null, null, null, null), Ct);
        roomResponse.StatusCode.Should().Be(HttpStatusCode.Created, await roomResponse.Content.ReadAsStringAsync(Ct));
        return (await roomResponse.Content.ReadFromJsonAsync<RoomDto>(Ct))!;
    }

    public static CreateBookingRequest Hourly(Guid roomId, DateOnly date, int startHour, int hours, bool confirm = true) =>
        new(roomId, null, null, 2, 0, "Гость Календаря", "+996555123456", null, null, null, null, confirm,
            Kind: StayKinds.Hourly, Date: date, StartHour: startHour, Hours: hours);

    public static async Task<HttpResponseMessage> PostAsync(HttpClient client, CreateBookingRequest request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/bookings") { Content = JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return await client.SendAsync(message, Ct);
    }

    public static async Task<BookingDto> CreateAsync(HttpClient client, CreateBookingRequest request)
    {
        var response = await PostAsync(client, request);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<BookingDto>(Ct))!;
    }

    public static RescheduleBookingRequest MoveHourly(Guid roomId, DateOnly date, int startHour, int hours, uint? version = null) =>
        new(roomId, StayKinds.Hourly, null, null, date, startHour, hours, version);

    /// <summary>Ближайший целый час, с которого можно начать бронь сегодня (или завтра в 00:00).</summary>
    public static (DateOnly Date, int Hour) NextHour(IHotelClock clock)
    {
        var next = clock.LocalNow.AddHours(1);
        return (DateOnly.FromDateTime(next.DateTime), next.Hour);
    }
}

/// <summary>Правила отмены и неявки, перенос брони и календарь (Sprint 5).</summary>
public class CalendarAndRulesTests(PostgresFixture db)
{
    private readonly TestApi _api = new(db);

    private IHotelClock Clock => db.Factory.Services.GetRequiredService<IHotelClock>();

    private DateOnly Today => Clock.Today;

    private async Task<HttpClient> OwnerAsync() => _api.Authorized((await _api.LoginOwnerAsync()).AccessToken);

    [Fact]
    public async Task CancelQuote_FarAhead_IsFree_ThenCancelWithoutPenalty()
    {
        var owner = await OwnerAsync();
        var room = await BookingTestKit.CreateRoomAsync(owner);
        var booking = await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(room.Id, Today.AddDays(20), 10, 2));

        var quote = (await owner.GetFromJsonAsync<CancellationQuoteDto>($"/api/v1/bookings/{booking.Id}/cancel/preview", Ct))!;
        quote.IsFree.Should().BeTrue();
        quote.FreeUntil.Should().Be($"{Today.AddDays(20):yyyy-MM-dd}T08:00", "бесплатно — за 2 часа до начала");
        quote.Unit.Should().Be("Hour");
        quote.NoShowPenalty.Should().Be(500m);

        var cancelled = await owner.PostAsJsonAsync($"/api/v1/bookings/{booking.Id}/cancel", new CancelBookingRequest("Планы изменились"), Ct);
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = (await cancelled.Content.ReadFromJsonAsync<BookingDto>(Ct))!;
        dto.PenaltyAmount.Should().Be(0m);
        dto.PenaltyKind.Should().BeNull();
    }

    [Fact]
    public async Task LateCancel_ChargesOneHour_AdminCannotWaive_OwnerCan()
    {
        var owner = await OwnerAsync();
        var room = await BookingTestKit.CreateRoomAsync(owner, 600m);
        var (date, hour) = BookingTestKit.NextHour(Clock);
        var first = await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(room.Id, date, hour, 2));
        var (_, adminTokens) = await _api.CreateActiveStaffAsync(Roles.Administrator);
        var admin = _api.Authorized(adminTokens.AccessToken);

        var quote = (await admin.GetFromJsonAsync<CancellationQuoteDto>($"/api/v1/bookings/{first.Id}/cancel/preview", Ct))!;
        quote.IsFree.Should().BeFalse("до начала меньше двух часов");
        quote.Penalty.Should().Be(600m);
        quote.CanWaive.Should().BeFalse("у администратора по умолчанию нет права «Отмена без штрафа»");

        var waive = await admin.PostAsJsonAsync($"/api/v1/bookings/{first.Id}/cancel", new CancelBookingRequest("Исключение", WaivePenalty: true), Ct);
        waive.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ProblemCodeAsync(waive)).Should().Be("booking.penalty_waive_forbidden");

        var charged = await admin.PostAsJsonAsync($"/api/v1/bookings/{first.Id}/cancel", new CancelBookingRequest("Гость отказался"), Ct);
        charged.StatusCode.Should().Be(HttpStatusCode.OK, await charged.Content.ReadAsStringAsync(Ct));
        var dto = (await charged.Content.ReadFromJsonAsync<BookingDto>(Ct))!;
        dto.PenaltyAmount.Should().Be(600m);
        dto.PenaltyKind.Should().Be("LateCancel");

        // Владелец может отменить без штрафа — штраф не начисляется, но исключение запоминается.
        var second = await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(room.Id, date, hour, 2));
        var waived = await owner.PostAsJsonAsync($"/api/v1/bookings/{second.Id}/cancel", new CancelBookingRequest("Постоянный гость", WaivePenalty: true), Ct);
        waived.StatusCode.Should().Be(HttpStatusCode.OK);
        var waivedDto = (await waived.Content.ReadFromJsonAsync<BookingDto>(Ct))!;
        waivedDto.PenaltyAmount.Should().Be(0m);
        waivedDto.PenaltyWaived.Should().BeTrue();
    }

    [Fact]
    public async Task PendingBooking_HasHold_NotLaterThanStart()
    {
        var owner = await OwnerAsync();
        var room = await BookingTestKit.CreateRoomAsync(owner);
        var far = await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(room.Id, Today.AddDays(10), 12, 2, confirm: false));

        far.Status.Should().Be("Pending");
        far.HoldUntil.Should().NotBeNull();
        var hold = DateTime.Parse(far.HoldUntil!, System.Globalization.CultureInfo.InvariantCulture);
        hold.Should().BeCloseTo(Clock.LocalNow.DateTime.AddMinutes(30), TimeSpan.FromMinutes(2));

        var confirmed = await owner.PostAsync(new Uri($"/api/v1/bookings/{far.Id}/confirm", UriKind.Relative), null, Ct);
        (await confirmed.Content.ReadFromJsonAsync<BookingDto>(Ct))!.HoldUntil.Should().BeNull();
    }

    [Fact]
    public async Task Reschedule_MovesToAnotherRoomAndTime_FreesOldSlot_Reprices()
    {
        var owner = await OwnerAsync();
        var roomA = await BookingTestKit.CreateRoomAsync(owner, 500m);
        var roomB = await BookingTestKit.CreateRoomAsync(owner, 700m);
        var day = Today.AddDays(25);
        var booking = await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(roomA.Id, day, 10, 2));
        booking.AllowedActions.Should().Contain("reschedule");

        var quoteResponse = await owner.PostAsJsonAsync($"/api/v1/bookings/{booking.Id}/reschedule/quote", BookingTestKit.MoveHourly(roomB.Id, day, 15, 3), Ct);
        quoteResponse.StatusCode.Should().Be(HttpStatusCode.OK, await quoteResponse.Content.ReadAsStringAsync(Ct));
        var quote = (await quoteResponse.Content.ReadFromJsonAsync<RescheduleQuoteDto>(Ct))!;
        quote.OldTotal.Should().Be(1000m);
        quote.NewTotal.Should().Be(2100m);
        quote.RoomNumber.Should().Be(roomB.Number);

        var moved = await owner.PostAsJsonAsync($"/api/v1/bookings/{booking.Id}/reschedule", BookingTestKit.MoveHourly(roomB.Id, day, 15, 3, booking.Version), Ct);
        moved.StatusCode.Should().Be(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync(Ct));
        var dto = (await moved.Content.ReadFromJsonAsync<BookingDto>(Ct))!;
        dto.Room.RoomId.Should().Be(roomB.Id);
        dto.StartsAt.Should().Be($"{day:yyyy-MM-dd}T15:00");
        dto.EndsAt.Should().Be($"{day:yyyy-MM-dd}T18:00");
        dto.Hours.Should().Be(3);
        dto.TotalAmount.Should().Be(2100m);

        // Прежнее время в номере A освободилось.
        await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(roomA.Id, day, 10, 2));

        var history = (await owner.GetFromJsonAsync<List<BookingHistoryDto>>($"/api/v1/bookings/{booking.Id}/history", Ct))!;
        history.Should().Contain(h => h.EventType == "rescheduled");
    }

    [Fact]
    public async Task Reschedule_OntoBusyTime_Conflicts_IncludingCleaning()
    {
        var owner = await OwnerAsync();
        var room = await BookingTestKit.CreateRoomAsync(owner);
        var day = Today.AddDays(26);
        await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(room.Id, day, 10, 2)); // занято 10:00–12:30 с уборкой
        var other = await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(room.Id, day, 16, 2));

        var busy = await owner.PostAsJsonAsync($"/api/v1/bookings/{other.Id}/reschedule", BookingTestKit.MoveHourly(room.Id, day, 12, 2), Ct);
        busy.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemCodeAsync(busy)).Should().Be("booking.room_unavailable");

        // Сдвиг внутри собственного времени брони — не конфликт с самой собой.
        var self = await owner.PostAsJsonAsync($"/api/v1/bookings/{other.Id}/reschedule", BookingTestKit.MoveHourly(room.Id, day, 17, 2), Ct);
        self.StatusCode.Should().Be(HttpStatusCode.OK, await self.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Reschedule_CannotChangeKind_OrMoveExtendedBooking()
    {
        var owner = await OwnerAsync();
        var room = await BookingTestKit.CreateRoomAsync(owner);
        var day = Today.AddDays(27);
        var booking = await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(room.Id, day, 10, 2));

        var nightly = await owner.PostAsJsonAsync($"/api/v1/bookings/{booking.Id}/reschedule",
            new RescheduleBookingRequest(room.Id, StayKinds.Nightly, day, day.AddDays(1), null, null, null), Ct);
        (await ProblemCodeAsync(nightly)).Should().Be("booking.reschedule_kind");

        var extended = await owner.PostAsJsonAsync($"/api/v1/bookings/{booking.Id}/extend", new ExtendBookingRequest(1, booking.Version), Ct);
        extended.StatusCode.Should().Be(HttpStatusCode.OK);
        var moved = await owner.PostAsJsonAsync($"/api/v1/bookings/{booking.Id}/reschedule", BookingTestKit.MoveHourly(room.Id, day, 14, 2), Ct);
        (await ProblemCodeAsync(moved)).Should().Be("booking.reschedule_extended");
    }

    [Fact]
    public async Task NoShow_Manual_OnlyAfterStart()
    {
        var owner = await OwnerAsync();
        var room = await BookingTestKit.CreateRoomAsync(owner);
        var booking = await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(room.Id, Today.AddDays(28), 10, 2));

        booking.AllowedActions.Should().NotContain("noShow");
        var early = await owner.PostAsJsonAsync($"/api/v1/bookings/{booking.Id}/no-show", new NoShowRequest(), Ct);
        (await ProblemCodeAsync(early)).Should().Be("booking.no_show_too_early");
    }

    [Fact]
    public async Task Calendar_ShowsRoomsAndActiveBookings_WithCleaning_NotCancelled()
    {
        var owner = await OwnerAsync();
        var room = await BookingTestKit.CreateRoomAsync(owner);
        var day = Today.AddDays(29);
        var active = await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(room.Id, day, 9, 3));
        var cancelled = await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(room.Id, day, 15, 2));
        await owner.PostAsJsonAsync($"/api/v1/bookings/{cancelled.Id}/cancel", new CancelBookingRequest("Тест"), Ct);

        var calendar = (await owner.GetFromJsonAsync<CalendarDto>($"/api/v1/calendar?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}", Ct))!;

        calendar.Rooms.Should().Contain(r => r.Id == room.Id && r.SellsHourly);
        var block = calendar.Bookings.Should().ContainSingle(b => b.RoomId == room.Id).Subject;
        block.Id.Should().Be(active.Id);
        block.StartsAt.Should().Be($"{day:yyyy-MM-dd}T09:00");
        block.EndsAt.Should().Be($"{day:yyyy-MM-dd}T12:00");
        block.BlockedUntil.Should().Be($"{day:yyyy-MM-dd}T12:30");
        block.CanReschedule.Should().BeTrue();
        calendar.CleaningBufferMinutes.Should().Be(30);
    }

    [Fact]
    public async Task Calendar_RangeLimited_AndForbiddenForHousekeeper()
    {
        var owner = await OwnerAsync();
        var tooLong = await owner.GetAsync(new Uri($"/api/v1/calendar?from={Today:yyyy-MM-dd}&to={Today.AddDays(40):yyyy-MM-dd}", UriKind.Relative), Ct);
        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(tooLong)).Should().Be("calendar.range");

        var (_, maidTokens) = await _api.CreateActiveStaffAsync(Roles.Housekeeper);
        var maid = _api.Authorized(maidTokens.AccessToken);
        (await maid.GetAsync(new Uri("/api/v1/calendar", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var room = await BookingTestKit.CreateRoomAsync(owner);
        var booking = await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(room.Id, Today.AddDays(30), 10, 2));
        (await maid.PostAsJsonAsync($"/api/v1/bookings/{booking.Id}/reschedule", BookingTestKit.MoveHourly(room.Id, Today.AddDays(30), 14, 2), Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await maid.PostAsJsonAsync($"/api/v1/bookings/{booking.Id}/no-show", new NoShowRequest(), Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}

/// <summary>
/// Фоновая задача истечения и неявки. Меняет настройку гостиницы и обрабатывает все брони — поэтому не параллельно с другими тестами.
/// </summary>
[Collection(nameof(GlobalSettingsGroup))]
public class BookingLifecycleTests(PostgresFixture db)
{
    private readonly TestApi _api = new(db);

    private IHotelClock Clock => db.Factory.Services.GetRequiredService<IHotelClock>();

    private async Task<HttpClient> OwnerAsync() => _api.Authorized((await _api.LoginOwnerAsync()).AccessToken);

    /// <summary>Сдвигает время брони в прошлое прямо в БД (через API бронь в прошлом не создать).</summary>
    private async Task ShiftAsync(Guid bookingId, string sql)
    {
        await using var scope = db.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DbContext>();
        await context.Database.ExecuteSqlRawAsync(sql.Replace("{id}", bookingId.ToString(), StringComparison.Ordinal), Ct);
    }

    private async Task<LifecycleRun> RunAsync()
    {
        await using var scope = db.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IBookingLifecycle>().RunAsync(Clock.UtcNow, Ct);
    }

    private static async Task<HotelSettingsDto> SetAutoNoShowAsync(HttpClient owner, bool enabled)
    {
        var settings = (await owner.GetFromJsonAsync<HotelSettingsDto>("/api/v1/hotel/settings", Ct))!;
        var response = await owner.PutAsJsonAsync("/api/v1/hotel/settings", HotelTests.ToUpdate(settings) with { AutoNoShowEnabled = enabled }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<HotelSettingsDto>(Ct))!;
    }

    [Fact]
    public async Task PendingBooking_ExpiresAfterHold_AndFreesRoom()
    {
        var owner = await OwnerAsync();
        var room = await BookingTestKit.CreateRoomAsync(owner);
        var day = Clock.Today.AddDays(5);
        var pending = await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(room.Id, day, 10, 2, confirm: false));

        await RunAsync();
        (await owner.GetFromJsonAsync<BookingDto>($"/api/v1/bookings/{pending.Id}", Ct))!.Status.Should().Be("Pending", "удержание ещё не истекло");

        await ShiftAsync(pending.Id, "UPDATE bookings.bookings SET hold_until = now() - interval '1 minute' WHERE id = '{id}'");
        var run = await RunAsync();

        run.Expired.Should().BeGreaterThanOrEqualTo(1);
        var expired = (await owner.GetFromJsonAsync<BookingDto>($"/api/v1/bookings/{pending.Id}", Ct))!;
        expired.Status.Should().Be("Expired");
        expired.HoldUntil.Should().BeNull();
        await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(room.Id, day, 10, 2)); // номер свободен
    }

    [Fact]
    public async Task AutoNoShow_OnlyWhenEnabled_ChargesPenalty()
    {
        var owner = await OwnerAsync();
        var room = await BookingTestKit.CreateRoomAsync(owner, 800m);
        var day = Clock.Today.AddDays(6);
        var booking = await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(room.Id, day, 10, 2));

        // «Гость должен был прийти 40 минут назад»: начало брони в прошлом.
        await ShiftAsync(booking.Id, "UPDATE bookings.bookings SET starts_at = now() - interval '40 minutes', ends_at = now() + interval '80 minutes' WHERE id = '{id}'");

        var settings = await SetAutoNoShowAsync(owner, false);
        settings.AutoNoShowEnabled.Should().BeFalse();
        await RunAsync();
        (await owner.GetFromJsonAsync<BookingDto>($"/api/v1/bookings/{booking.Id}", Ct))!.Status.Should().Be("Confirmed", "автонеявка выключена по умолчанию");

        await SetAutoNoShowAsync(owner, true);
        try
        {
            var run = await RunAsync();
            run.NoShows.Should().BeGreaterThanOrEqualTo(1);
            var dto = (await owner.GetFromJsonAsync<BookingDto>($"/api/v1/bookings/{booking.Id}", Ct))!;
            dto.Status.Should().Be("NoShow");
            dto.PenaltyAmount.Should().Be(800m, "штраф — 1 час по цене брони");
            dto.PenaltyKind.Should().Be("NoShow");
        }
        finally
        {
            await SetAutoNoShowAsync(owner, false);
        }
    }

    [Fact]
    public async Task ManualNoShow_AfterStart_FreesRoom()
    {
        var owner = await OwnerAsync();
        var room = await BookingTestKit.CreateRoomAsync(owner);
        var day = Clock.Today.AddDays(7);
        var booking = await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(room.Id, day, 10, 2));
        await ShiftAsync(booking.Id, "UPDATE bookings.bookings SET starts_at = now() - interval '5 minutes', ends_at = now() + interval '115 minutes' WHERE id = '{id}'");

        var marked = await owner.PostAsJsonAsync($"/api/v1/bookings/{booking.Id}/no-show", new NoShowRequest(WaivePenalty: true), Ct);

        marked.StatusCode.Should().Be(HttpStatusCode.OK, await marked.Content.ReadAsStringAsync(Ct));
        var dto = (await marked.Content.ReadFromJsonAsync<BookingDto>(Ct))!;
        dto.Status.Should().Be("NoShow");
        dto.PenaltyWaived.Should().BeTrue();
        await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(room.Id, day, 10, 2)); // номер освобождён
    }
}
