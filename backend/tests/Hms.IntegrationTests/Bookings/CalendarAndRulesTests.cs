using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hms.IntegrationTests.Hotel;
using Hms.Modules.Bookings.Application;
using Hms.Modules.Hotel.Application;
using Hms.Modules.Identity.Contracts;
using Hms.Modules.Rooms.Application;
using Hms.Platform.Time;
using Hms.SharedKernel;
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

    /// <summary>Штрафы гостиницы: 1 час / 1 ночь или выключены (по умолчанию). Меняет глобальные настройки — только в GlobalSettingsGroup.</summary>
    public static async Task SetPenaltiesAsync(HttpClient owner, bool enabled)
    {
        var units = enabled ? 1 : 0;
        var settings = (await owner.GetFromJsonAsync<HotelSettingsDto>("/api/v1/hotel/settings", Ct))!;
        var response = await owner.PutAsJsonAsync("/api/v1/hotel/settings", HotelTests.ToUpdate(settings) with
        {
            CancellationPolicy = settings.CancellationPolicy with { LateCancelPenaltyNights = units, NoShowPenaltyNights = units },
            HourlyPolicy = settings.HourlyPolicy with { LateCancelPenaltyHours = units, NoShowPenaltyHours = units },
        }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

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

    [Fact]
    public async Task RescheduleRooms_OnlyFreeAtNewTime_OwnRoomStaysAvailable()
    {
        var owner = _api.Authorized((await _api.LoginOwnerAsync()).AccessToken);
        var day = Clock.Today.AddDays(45);
        var first = await BookingTestKit.CreateRoomAsync(owner, 500m);
        var second = await BookingTestKit.CreateRoomAsync(owner, 500m);
        await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(first.Id, day, 10, 2));
        var moving = await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(second.Id, day, 10, 2));

        async Task<List<Guid>> FreeAsync(int hour) =>
            (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/bookings/{moving.Id}/reschedule/rooms?date={day:yyyy-MM-dd}&startHour={hour}&hours=2", Ct))
                .GetProperty("roomIds").EnumerateArray().Select(x => x.GetGuid()).ToList();

        var same = await FreeAsync(10);
        same.Should().Contain(second.Id, "переносимая бронь свой номер не занимает");
        same.Should().NotContain(first.Id, "в 10:00 первый номер занят другой бронью");
        var later = await FreeAsync(14);
        later.Should().Contain([first.Id, second.Id]);
        // Уборка после брони (по умолчанию 30 мин): с 12:00 первый номер ещё на уборке.
        (await FreeAsync(12)).Should().NotContain(first.Id);
    }

    [Fact]
    public async Task Reschedule_WithoutChanges_IsRejected()
    {
        var owner = await OwnerAsync();
        var day = Clock.Today.AddDays(46);
        var room = await BookingTestKit.CreateRoomAsync(owner, 500m);
        var booking = await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(room.Id, day, 10, 2));

        var same = await owner.PostAsJsonAsync($"/api/v1/bookings/{booking.Id}/reschedule/quote", BookingTestKit.MoveHourly(room.Id, day, 10, 2), Ct);
        (await ProblemCodeAsync(same)).Should().Be("booking.reschedule_no_changes");

        var later = await owner.PostAsJsonAsync($"/api/v1/bookings/{booking.Id}/reschedule/quote", BookingTestKit.MoveHourly(room.Id, day, 11, 2), Ct);
        later.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private DateOnly Today => Clock.Today;

    private async Task<HttpClient> OwnerAsync() => _api.Authorized((await _api.LoginOwnerAsync()).AccessToken);

    [Fact]
    public async Task ByDefault_NoPenalties_EvenLateCancelIsFree()
    {
        var owner = await OwnerAsync();
        var room = await BookingTestKit.CreateRoomAsync(owner, 600m);
        var (date, hour) = BookingTestKit.NextHour(Clock);
        var booking = await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(room.Id, date, hour, 2));

        var quote = (await owner.GetFromJsonAsync<CancellationQuoteDto>($"/api/v1/bookings/{booking.Id}/cancel/preview", Ct))!;
        quote.Should().BeEquivalentTo(new { IsFree = true, FreeUntil = (string?)null, Penalty = 0m, NoShowPenalty = 0m }, "штрафы по умолчанию выключены");

        var cancelled = await owner.PostAsJsonAsync($"/api/v1/bookings/{booking.Id}/cancel", new CancelBookingRequest("Планы изменились"), Ct);
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = (await cancelled.Content.ReadFromJsonAsync<BookingDto>(Ct))!;
        dto.PenaltyAmount.Should().Be(0m);
        dto.PenaltyKind.Should().BeNull();
    }

    [Fact]
    public async Task BookingNumbers_KindAndCreationDay_DailyCounter_Searchable()
    {
        var owner = await OwnerAsync();
        var room = await BookingTestKit.CreateRoomAsync(owner);
        var hourly = await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(room.Id, Today.AddDays(33), 10, 2));
        var nightly = await BookingTestKit.CreateAsync(owner,
            new CreateBookingRequest(room.Id, Today.AddDays(34), Today.AddDays(35), 2, 0, "Гость", "+996555123456", null, null, null, null, true));
        var day = Today.ToString("ddMM", System.Globalization.CultureInfo.InvariantCulture);

        hourly.Number.Should().MatchRegex($@"^Ч-{day}-\d{{2,}}$", "почасовая, создана сегодня");
        nightly.Number.Should().MatchRegex($@"^С-{day}-\d{{2,}}$", "посуточная, создана сегодня");
        int Seq(string number) => int.Parse(number.Split('-')[2], System.Globalization.CultureInfo.InvariantCulture);
        Seq(nightly.Number).Should().BeGreaterThan(Seq(hourly.Number), "счётчик за день общий для обоих видов");

        foreach (var search in new[] { hourly.Number, hourly.Number.ToLowerInvariant(), nightly.Number.Replace('С', 'C') })
        {
            var page = (await owner.GetFromJsonAsync<PagedResult<BookingListItemDto>>($"/api/v1/bookings?search={Uri.EscapeDataString(search)}", Ct))!;
            page.Items.Should().Contain(b => b.Number == search || b.Id == nightly.Id || b.Id == hourly.Id, $"поиск «{search}»");
        }
    }

    [Fact]
    public async Task ParallelBookings_GetDistinctNumbers()
    {
        var owner = await OwnerAsync();
        var rooms = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => BookingTestKit.CreateRoomAsync(owner)));
        var created = await Task.WhenAll(rooms.Select(r => BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(r.Id, Today.AddDays(36), 10, 2))));

        created.Select(b => b.Number).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task BookingSearch_FindsPhoneInLocalFormat()
    {
        var owner = await OwnerAsync();
        var room = await BookingTestKit.CreateRoomAsync(owner);
        var phone = UniquePhone(); // +9967XXXXXXXX
        var booking = await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(room.Id, Today.AddDays(31), 10, 2) with { GuestPhone = phone });
        var local = "0" + phone[4..7] + " " + phone[7..10]; // «0700 123» — так номер называют по телефону

        var page = (await owner.GetFromJsonAsync<PagedResult<BookingListItemDto>>($"/api/v1/bookings?search={Uri.EscapeDataString(local)}", Ct))!;

        page.Items.Should().Contain(b => b.Id == booking.Id);
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
    public async Task Reschedule_CannotChangeKind_ButExtendedBookingMovesAsOneBlock()
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
        var afterExtend = (await extended.Content.ReadFromJsonAsync<BookingDto>(Ct))!;
        afterExtend.AllowedActions.Should().Contain("reschedule");

        // 3 часа (2 + продление 1) переносятся одним блоком и пересчитываются по текущей цене часа.
        var moved = await owner.PostAsJsonAsync($"/api/v1/bookings/{booking.Id}/reschedule", BookingTestKit.MoveHourly(room.Id, day, 14, 3, afterExtend.Version), Ct);
        moved.StatusCode.Should().Be(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync(Ct));
        var dto = (await moved.Content.ReadFromJsonAsync<BookingDto>(Ct))!;
        dto.Hours.Should().Be(3);
        dto.TotalAmount.Should().Be(1500m);
        dto.ExtensionsAmount.Should().Be(0m);
        dto.Extensions.Should().BeEmpty("прежнее продление уже вошло в новую цену");
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
        block.CanExtend.Should().BeTrue();
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

    [Fact]
    public async Task Penalties_WhenOwnerEnablesThem_LateCancelChargesOneHour_AdminCannotWaive_OwnerCan()
    {
        var owner = await OwnerAsync();
        await BookingTestKit.SetPenaltiesAsync(owner, true);
        try
        {
            var room = await BookingTestKit.CreateRoomAsync(owner, 600m);
            var far = await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(room.Id, Clock.Today.AddDays(20), 10, 2));
            var farQuote = (await owner.GetFromJsonAsync<CancellationQuoteDto>($"/api/v1/bookings/{far.Id}/cancel/preview", Ct))!;
            farQuote.IsFree.Should().BeTrue();
            farQuote.FreeUntil.Should().Be($"{Clock.Today.AddDays(20):yyyy-MM-dd}T08:00", "бесплатно — за 2 часа до начала");
            farQuote.NoShowPenalty.Should().Be(600m);

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

            // Начисленный штраф — к оплате и в долгах.
            var balance = (await owner.GetFromJsonAsync<Hms.Modules.Finance.Application.BookingBalanceDto>($"/api/v1/bookings/{first.Id}/balance", Ct))!;
            balance.Due.Should().Be(600m);
            var debts = (await owner.GetFromJsonAsync<Hms.Modules.Finance.Application.DebtListDto>("/api/v1/finance/debts", Ct))!;
            debts.Items.Should().Contain(d => d.BookingId == first.Id && d.Balance == 600m && d.BookingStatus == "Cancelled");
        }
        finally
        {
            await BookingTestKit.SetPenaltiesAsync(owner, false);
        }
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
        await BookingTestKit.SetPenaltiesAsync(owner, true);
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
            await BookingTestKit.SetPenaltiesAsync(owner, false);
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
        dto.PenaltyAmount.Should().Be(0m, "штрафы по умолчанию выключены — снимать нечего");
        dto.PenaltyWaived.Should().BeFalse();
        await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(room.Id, day, 10, 2)); // номер освобождён
    }
}
