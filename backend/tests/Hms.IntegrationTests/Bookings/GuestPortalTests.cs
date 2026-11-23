using System.Net;
using System.Net.Http.Json;
using Hms.IntegrationTests.Hotel;
using Hms.Modules.Bookings.Application;
using Hms.Modules.Finance.Application;
using Hms.Modules.Guests.Application;
using Hms.Modules.Guests.Contracts;
using Hms.Modules.Hotel.Application;
using Hms.Modules.Identity.Application;
using Hms.Platform.Time;
using Microsoft.Extensions.DependencyInjection;
using static Hms.IntegrationTests.TestApi;

namespace Hms.IntegrationTests.Bookings;

/// <summary>
/// Кабинет гостя (Sprint 8): бронь с сайта, только свои брони/платежи/возвраты (IDOR), отмена, продление,
/// запрос возврата, профиль, согласия, удаление аккаунта. Меняет «авто-подтверждение веб-броней» — в группе настроек.
/// </summary>
[Collection(nameof(GlobalSettingsGroup))]
public class GuestPortalTests(PostgresFixture db)
{
    private readonly TestApi _api = new(db);

    private IHotelClock Clock => db.Factory.Services.GetRequiredService<IHotelClock>();

    private async Task<HttpClient> OwnerAsync() => _api.Authorized((await _api.LoginOwnerAsync()).AccessToken);

    private async Task<(HttpClient Client, string Email)> GuestAsync()
    {
        var (email, _) = await _api.RegisterConfirmedGuestAsync();
        return (_api.Authorized((await _api.LoginAsync(email, GuestPassword)).AccessToken), email);
    }

    private static async Task<HttpResponseMessage> PostIdempotentAsync<T>(HttpClient client, string url, T body, string? key = null)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        message.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N"));
        return await client.SendAsync(message, Ct);
    }

    private GuestCreateBookingRequest Hourly(Guid roomId, int daysAhead = 30, int hour = 12, int hours = 2) =>
        new(roomId, "Hourly", null, null, Clock.Today.AddDays(daysAhead), hour, hours, 2, 0, null, null, "Поздний заезд");

    private static async Task<MyBookingDto> CreateAsync(HttpClient guest, GuestCreateBookingRequest request)
    {
        var response = await PostIdempotentAsync(guest, "/api/v1/me/bookings", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<MyBookingDto>(Ct))!;
    }

    private static async Task SetAutoConfirmAsync(HttpClient owner, bool enabled)
    {
        var settings = (await owner.GetFromJsonAsync<HotelSettingsDto>("/api/v1/hotel/settings", Ct))!;
        var response = await owner.PutAsJsonAsync("/api/v1/hotel/settings", HotelTests.ToUpdate(settings) with { AutoConfirmWebBookings = enabled }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    private static async Task PayAsync(HttpClient owner, Guid bookingId, decimal amount)
    {
        var response = await PostIdempotentAsync(owner, "/api/v1/payments/manual", new RegisterPaymentRequest(bookingId, "Prepayment", "MBank", amount, null));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task WebBooking_IsPendingWithHold_ForChosenRoom_AndVisibleOnlyToOwner()
    {
        var owner = await OwnerAsync();
        await SetAutoConfirmAsync(owner, false);
        var room = await BookingTestKit.CreateRoomAsync(owner, 500m);
        var (guest, email) = await GuestAsync();

        var booking = await CreateAsync(guest, Hourly(room.Id));

        booking.Status.Should().Be("Pending");
        booking.HoldUntil.Should().NotBeNull("неподтверждённая веб-бронь держит номер ограниченное время");
        booking.TotalAmount.Should().Be(1000m);
        booking.RoomTypeId.Should().Be(room.RoomTypeId);
        booking.RoomNumber.Should().Be(room.Number, "гость сам выбрал номер на сайте");
        booking.AllowedActions.Should().Contain("cancel");

        // Администратор видит веб-бронь с контактом гостя из профиля.
        var staffView = (await owner.GetFromJsonAsync<BookingDto>($"/api/v1/bookings/{booking.Id}", Ct))!;
        staffView.Source.Should().Be("Web");
        staffView.GuestEmail.Should().Be(email);
        staffView.Comment.Should().Be("Поздний заезд");

        (await guest.GetFromJsonAsync<List<MyBookingDto>>("/api/v1/me/bookings", Ct))!.Should().ContainSingle(b => b.Id == booking.Id);
        (await guest.GetFromJsonAsync<List<MyBookingDto>>("/api/v1/me/bookings?scope=history", Ct))!.Should().BeEmpty();

        // Чужой гость: бронь «не найдена» везде — просмотр, баланс, отмена, продление.
        var (stranger, _) = await GuestAsync();
        (await stranger.GetAsync(new Uri($"/api/v1/me/bookings/{booking.Id}", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await stranger.GetAsync(new Uri($"/api/v1/me/bookings/{booking.Id}/balance", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await stranger.GetAsync(new Uri($"/api/v1/me/bookings/{booking.Id}/cancel/preview", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await stranger.PostAsJsonAsync($"/api/v1/me/bookings/{booking.Id}/cancel", new GuestCancelRequest(null), Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await PostIdempotentAsync(stranger, $"/api/v1/me/bookings/{booking.Id}/extend", new ExtendBookingRequest(1, null))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await PostIdempotentAsync(stranger, "/api/v1/me/refunds", new MyRefundRequest(booking.Id, 100m, "Чужая"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await stranger.GetFromJsonAsync<List<MyBookingDto>>("/api/v1/me/bookings", Ct))!.Should().BeEmpty();

        // Бронь всё ещё действует — чужие запросы ничего не изменили.
        (await guest.GetFromJsonAsync<MyBookingDto>($"/api/v1/me/bookings/{booking.Id}", Ct))!.Status.Should().Be("Pending");

        // Гостю закрыты служебные эндпоинты.
        (await guest.GetAsync(new Uri($"/api/v1/bookings/{booking.Id}", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await guest.GetAsync(new Uri("/api/v1/payments", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AutoConfirm_ConfirmsWebBooking_AndAnonymousCannotBook()
    {
        var owner = await OwnerAsync();
        await SetAutoConfirmAsync(owner, true);
        try
        {
            var room = await BookingTestKit.CreateRoomAsync(owner, 500m);
            var (guest, _) = await GuestAsync();
            (await CreateAsync(guest, Hourly(room.Id, 31))).Status.Should().Be("Confirmed");

            var anonymous = await PostIdempotentAsync(_api.Anonymous(), "/api/v1/me/bookings", Hourly(room.Id, 32));
            anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally
        {
            await SetAutoConfirmAsync(owner, false);
        }
    }

    [Fact]
    public async Task WebBooking_ChosenRoomTaken_IsConflict_WithoutSubstitutingAnotherRoom()
    {
        var owner = await OwnerAsync();
        var room = await BookingTestKit.CreateRoomAsync(owner, 500m);
        var (guest, _) = await GuestAsync();
        var request = Hourly(room.Id, 33);

        (await CreateAsync(guest, request)).RoomNumber.Should().Be(room.Number);
        var second = await PostIdempotentAsync(guest, "/api/v1/me/bookings", request);

        // Номер выбран гостем: занят — 409, другой номер система не подставляет.
        (await ProblemCodeAsync(second)).Should().Be("booking.room_unavailable");

        // Расчёт цены на сайте для занятого номера — тоже 409; несуществующий номер — 404.
        var quote = new PublicQuoteRequest(room.Id, null, null, 2, 0, null, null, "Hourly", request.Date, request.StartHour, request.Hours);
        (await ProblemCodeAsync(await _api.Anonymous().PostAsJsonAsync("/api/v1/public/bookings/quote", quote, Ct))).Should().Be("booking.room_unavailable");
        (await _api.Anonymous().PostAsJsonAsync("/api/v1/public/bookings/quote", quote with { RoomId = Guid.NewGuid() }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        var free = await _api.Anonymous().PostAsJsonAsync("/api/v1/public/bookings/quote", quote with { Date = request.Date!.Value.AddDays(1) }, Ct);
        free.StatusCode.Should().Be(HttpStatusCode.OK, await free.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Guest_CancelsOwnBooking_AndRequestsRefundOfOverpayment_OnlyOnce()
    {
        var owner = await OwnerAsync();
        var room = await BookingTestKit.CreateRoomAsync(owner, 500m);
        var (guest, _) = await GuestAsync();
        var booking = await CreateAsync(guest, Hourly(room.Id, 34));
        (await owner.PostAsync(new Uri($"/api/v1/bookings/{booking.Id}/confirm", UriKind.Relative), null, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        await PayAsync(owner, booking.Id, 1000m);

        var terms = (await guest.GetFromJsonAsync<CancellationQuoteDto>($"/api/v1/me/bookings/{booking.Id}/cancel/preview", Ct))!;
        terms.IsFree.Should().BeTrue("штрафов по умолчанию нет");
        terms.CanWaive.Should().BeFalse();

        // Пока бронь оплачена и действует — переплаты нет.
        (await ProblemCodeAsync(await PostIdempotentAsync(guest, "/api/v1/me/refunds", new MyRefundRequest(booking.Id, 100m, "Рано"))))
            .Should().Be("refund.nothing_to_refund");

        var cancelled = await guest.PostAsJsonAsync($"/api/v1/me/bookings/{booking.Id}/cancel", new GuestCancelRequest("Изменились планы"), Ct);
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync(Ct));
        (await cancelled.Content.ReadFromJsonAsync<MyBookingDto>(Ct))!.Status.Should().Be("Cancelled");

        var balance = (await guest.GetFromJsonAsync<MyBookingBalanceDto>($"/api/v1/me/bookings/{booking.Id}/balance", Ct))!;
        balance.Balance.Should().Be(-1000m);
        balance.Refundable.Should().Be(1000m);

        (await ProblemCodeAsync(await PostIdempotentAsync(guest, "/api/v1/me/refunds", new MyRefundRequest(booking.Id, 1500m, "Больше"))))
            .Should().Be("refund.amount_exceeds_overpayment");
        var requested = await PostIdempotentAsync(guest, "/api/v1/me/refunds", new MyRefundRequest(booking.Id, 1000m, "Отменил бронь"));
        requested.StatusCode.Should().Be(HttpStatusCode.Created, await requested.Content.ReadAsStringAsync(Ct));
        (await ProblemCodeAsync(await PostIdempotentAsync(guest, "/api/v1/me/refunds", new MyRefundRequest(booking.Id, 1m, "Ещё"))))
            .Should().Be("refund.nothing_to_refund", "вся переплата уже запрошена");

        var refunds = (await guest.GetFromJsonAsync<List<MyRefundDto>>("/api/v1/me/refunds", Ct))!;
        refunds.Should().ContainSingle(r => r.BookingId == booking.Id && r.Amount == 1000m && r.Status == "Requested");
        var payments = (await guest.GetFromJsonAsync<List<MyPaymentDto>>("/api/v1/me/payments", Ct))!;
        payments.Should().ContainSingle(p => p.BookingId == booking.Id && p.Amount == 1000m && p.Method == "MBank");

        // Запрос виден администратору в «Возвратах».
        (await owner.GetFromJsonAsync<RefundPageDto>("/api/v1/refunds?status=Requested&pageSize=200", Ct))!.Page.Items
            .Should().Contain(r => r.BookingId == booking.Id && r.Reason.Contains("Отменил бронь"));

        (await guest.GetFromJsonAsync<List<MyBookingDto>>("/api/v1/me/bookings?scope=history", Ct))!.Should().Contain(b => b.Id == booking.Id);
    }

    [Fact]
    public async Task Guest_ExtendsOwnBooking()
    {
        var owner = await OwnerAsync();
        var room = await BookingTestKit.CreateRoomAsync(owner, 500m);
        var (guest, _) = await GuestAsync();
        var booking = await CreateAsync(guest, Hourly(room.Id, 35));
        booking.AllowedActions.Should().NotContain("extend", "неподтверждённую бронь не продлевают");
        var confirmed = await owner.PostAsync(new Uri($"/api/v1/bookings/{booking.Id}/confirm", UriKind.Relative), null, Ct);
        confirmed.StatusCode.Should().Be(HttpStatusCode.OK);
        booking = (await guest.GetFromJsonAsync<MyBookingDto>($"/api/v1/me/bookings/{booking.Id}", Ct))!;
        booking.AllowedActions.Should().Contain("extend");

        var quote = (await guest.GetFromJsonAsync<ExtensionQuoteDto>($"/api/v1/me/bookings/{booking.Id}/extend/quote?units=1", Ct))!;
        quote.Amount.Should().Be(500m);
        var extended = await PostIdempotentAsync(guest, $"/api/v1/me/bookings/{booking.Id}/extend", new ExtendBookingRequest(1, booking.Version));
        extended.StatusCode.Should().Be(HttpStatusCode.OK, await extended.Content.ReadAsStringAsync(Ct));
        var result = (await extended.Content.ReadFromJsonAsync<MyBookingDto>(Ct))!;
        result.TotalAmount.Should().Be(1500m);
        result.Extensions.Should().ContainSingle(x => x.Quantity == 1 && x.Amount == 500m);
    }

    [Fact]
    public async Task Profile_SyncsGuestCard_Consents_AndDeleteAccount()
    {
        var owner = await OwnerAsync();
        var room = await BookingTestKit.CreateRoomAsync(owner, 500m);
        var (guest, email) = await GuestAsync();

        var phone = UniquePhone();
        (await guest.PutAsJsonAsync("/api/v1/me", new UpdateMeRequest("Айбек Профильный", phone), Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        var booking = await CreateAsync(guest, Hourly(room.Id, 36));
        var staffView = (await owner.GetFromJsonAsync<BookingDto>($"/api/v1/bookings/{booking.Id}", Ct))!;
        staffView.GuestName.Should().Be("Айбек Профильный");
        staffView.GuestPhone.Should().NotBeNull();

        var consents = (await guest.GetFromJsonAsync<PortalConsents>("/api/v1/me/consents", Ct))!;
        consents.PersonalData.Should().BeTrue();
        consents.WhatsApp.Should().BeFalse();
        var granted = await guest.PutAsJsonAsync("/api/v1/me/consents", new SetMyConsentsRequest(true), Ct);
        (await granted.Content.ReadFromJsonAsync<PortalConsents>(Ct))!.WhatsApp.Should().BeTrue();

        // Удаление: неверный пароль; действующая бронь мешает; после отмены — аккаунт удалён, карточка и бронь обезличены.
        using var wrong = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/me") { Content = JsonContent.Create(new DeleteAccountRequest("неверный")) };
        (await ProblemCodeAsync(await guest.SendAsync(wrong, Ct))).Should().Be("account.wrong_password");
        using var active = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/me") { Content = JsonContent.Create(new DeleteAccountRequest(GuestPassword)) };
        (await ProblemCodeAsync(await guest.SendAsync(active, Ct))).Should().Be("account.active_bookings");

        (await guest.PostAsJsonAsync($"/api/v1/me/bookings/{booking.Id}/cancel", new GuestCancelRequest(null), Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        using var delete = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/me") { Content = JsonContent.Create(new DeleteAccountRequest(GuestPassword)) };
        var deleted = await guest.SendAsync(delete, Ct);
        deleted.IsSuccessStatusCode.Should().BeTrue(await deleted.Content.ReadAsStringAsync(Ct));

        (await _api.LoginRawAsync(email, GuestPassword)).StatusCode.Should().NotBe(HttpStatusCode.OK);
        var after = (await owner.GetFromJsonAsync<BookingDto>($"/api/v1/bookings/{booking.Id}", Ct))!;
        after.GuestName.Should().Be("Гость удалён");
        after.GuestEmail.Should().BeNull();
    }

    [Fact]
    public async Task Staff_CannotDeleteOwnAccount_FromCabinet()
    {
        var (_, tokens) = await _api.CreateActiveStaffAsync(Hms.Modules.Identity.Contracts.Roles.Administrator);
        var admin = _api.Authorized(tokens.AccessToken);
        using var delete = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/me") { Content = JsonContent.Create(new DeleteAccountRequest(StaffPassword)) };
        (await ProblemCodeAsync(await admin.SendAsync(delete, Ct))).Should().Be("account.staff_cannot_delete");
    }
}
