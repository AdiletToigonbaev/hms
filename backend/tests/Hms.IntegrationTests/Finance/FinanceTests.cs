using System.Net;
using System.Net.Http.Json;
using Hms.IntegrationTests.Bookings;
using Hms.Modules.Bookings.Application;
using Hms.Modules.Finance.Application;
using Hms.Modules.Identity.Application;
using Hms.Modules.Identity.Contracts;
using Hms.Platform.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Hms.IntegrationTests.TestApi;

namespace Hms.IntegrationTests.Finance;

/// <summary>Платежи, баланс брони, сторно, возвраты с лимитом, долги и расходы (Sprint 6).</summary>
public class FinanceTests(PostgresFixture db)
{
    private readonly TestApi _api = new(db);

    private IHotelClock Clock => db.Factory.Services.GetRequiredService<IHotelClock>();

    private async Task<HttpClient> OwnerAsync() => _api.Authorized((await _api.LoginOwnerAsync()).AccessToken);

    /// <summary>Почасовая бронь на 2 часа по 500 = 1000 сом, далеко в будущем.</summary>
    private async Task<BookingDto> BookingAsync(HttpClient owner, int daysAhead = 40, int hour = 10)
    {
        var room = await BookingTestKit.CreateRoomAsync(owner, 500m);
        return await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(room.Id, Clock.Today.AddDays(daysAhead), hour, 2));
    }

    private static async Task<HttpResponseMessage> PayAsync(HttpClient client, Guid? bookingId, decimal amount, string type = "Stay", string method = "Cash", string? key = null)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/manual")
        {
            Content = JsonContent.Create(new RegisterPaymentRequest(bookingId, type, method, amount, null)),
        };
        message.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N"));
        return await client.SendAsync(message, Ct);
    }

    private static async Task<HttpResponseMessage> PostIdempotentAsync<T>(HttpClient client, string url, T body)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        message.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return await client.SendAsync(message, Ct);
    }

    private static async Task<BookingBalanceDto> BalanceAsync(HttpClient client, Guid bookingId) =>
        (await client.GetFromJsonAsync<BookingBalanceDto>($"/api/v1/bookings/{bookingId}/balance", Ct))!;

    [Fact]
    public async Task Payments_ReduceBalance_AndOverpaymentIsRejected()
    {
        var owner = await OwnerAsync();
        var booking = await BookingAsync(owner);

        (await PayAsync(owner, booking.Id, 600m, "Prepayment")).StatusCode.Should().Be(HttpStatusCode.Created);
        var balance = await BalanceAsync(owner, booking.Id);
        balance.Should().BeEquivalentTo(new { Due = 1000m, Received = 600m, Balance = 400m });

        var over = await PayAsync(owner, booking.Id, 500m);
        over.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ProblemCodeAsync(over)).Should().Be("payment.overpayment");

        (await PayAsync(owner, booking.Id, 400m, method: "CardPos")).StatusCode.Should().Be(HttpStatusCode.Created);
        (await BalanceAsync(owner, booking.Id)).Balance.Should().Be(0m);
        (await ProblemCodeAsync(await PayAsync(owner, booking.Id, 1m))).Should().Be("payment.nothing_due");
    }

    [Fact]
    public async Task Payment_WithoutBooking_OnlyOther()
    {
        var owner = await OwnerAsync();

        (await ProblemCodeAsync(await PayAsync(owner, null, 100m, "Stay"))).Should().Be("payment.booking_required");
        (await PayAsync(owner, null, 150m, "Other")).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Payment_RepeatWithSameKey_IsRegisteredOnce()
    {
        var owner = await OwnerAsync();
        var booking = await BookingAsync(owner);
        var key = Guid.NewGuid().ToString("N");

        var first = await PayAsync(owner, booking.Id, 300m, key: key);
        var second = await PayAsync(owner, booking.Id, 300m, key: key);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        (await BalanceAsync(owner, booking.Id)).Payments.Should().ContainSingle();
    }

    [Fact]
    public async Task ParallelPayments_NeverExceedWhatIsDue()
    {
        var owner = await OwnerAsync();
        var booking = await BookingAsync(owner);

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PayAsync(owner, booking.Id, 600m)));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        (await BalanceAsync(owner, booking.Id)).Received.Should().Be(600m);
    }

    [Fact]
    public async Task Reversal_RestoresBalance_OnlyOnce_AndAdminCannotReverse()
    {
        var owner = await OwnerAsync();
        var booking = await BookingAsync(owner);
        var paid = (await (await PayAsync(owner, booking.Id, 1000m)).Content.ReadFromJsonAsync<PaymentDto>(Ct))!;
        paid.AllowedActions.Should().Contain("reverse");

        var (_, adminTokens) = await _api.CreateActiveStaffAsync(Roles.Administrator);
        var admin = _api.Authorized(adminTokens.AccessToken);
        (await PostIdempotentAsync(admin, $"/api/v1/payments/{paid.Payment.Id}/reverse", new ReversePaymentRequest("Ошибка"))).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        var reversed = await PostIdempotentAsync(owner, $"/api/v1/payments/{paid.Payment.Id}/reverse", new ReversePaymentRequest("Ошиблись суммой"));
        reversed.StatusCode.Should().Be(HttpStatusCode.OK, await reversed.Content.ReadAsStringAsync(Ct));
        (await reversed.Content.ReadFromJsonAsync<PaymentDto>(Ct))!.Payment.Status.Should().Be("Reversed");

        var balance = await BalanceAsync(owner, booking.Id);
        balance.Balance.Should().Be(1000m);
        balance.Payments.Should().Contain(p => p.Type == "Reversal" && p.Amount == -1000m);

        var again = await PostIdempotentAsync(owner, $"/api/v1/payments/{paid.Payment.Id}/reverse", new ReversePaymentRequest("Ещё раз"));
        (await ProblemCodeAsync(again)).Should().Be("payment.already_reversed");
    }

    [Fact]
    public async Task Payments_AreImmutable_AtDatabaseLevel()
    {
        var owner = await OwnerAsync();
        var booking = await BookingAsync(owner);
        var paid = (await (await PayAsync(owner, booking.Id, 500m)).Content.ReadFromJsonAsync<PaymentDto>(Ct))!;

        await using var scope = db.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DbContext>();
        var id = paid.Payment.Id;
        var update = () => context.Database.ExecuteSqlAsync($"UPDATE finance.payments SET amount = 1 WHERE id = {id}", Ct);
        var delete = () => context.Database.ExecuteSqlAsync($"DELETE FROM finance.payments WHERE id = {id}", Ct);
        var log = () => context.Database.ExecuteSqlAsync($"DELETE FROM finance.payment_transactions WHERE payment_id = {id}", Ct);

        await update.Should().ThrowAsync<Npgsql.PostgresException>();
        await delete.Should().ThrowAsync<Npgsql.PostgresException>();
        await log.Should().ThrowAsync<Npgsql.PostgresException>();
    }

    [Fact]
    public async Task Refund_Flow_WithApproverLimit_AndOverpaidDebtView()
    {
        var owner = await OwnerAsync();
        var booking = await BookingAsync(owner);
        var paid = (await (await PayAsync(owner, booking.Id, 1000m, "Prepayment")).Content.ReadFromJsonAsync<PaymentDto>(Ct))!;
        // Гость отменил заранее — без штрафа: вся предоплата — переплата, нужен возврат.
        await owner.PostAsJsonAsync($"/api/v1/bookings/{booking.Id}/cancel", new CancelBookingRequest("Гость передумал"), Ct);
        var overpaid = (await owner.GetFromJsonAsync<DebtListDto>("/api/v1/finance/debts?scope=overpaid", Ct))!;
        overpaid.Items.Should().Contain(d => d.BookingId == booking.Id && d.Balance == -1000m);

        var (adminStaff, adminTokens) = await _api.CreateActiveStaffAsync(Roles.Administrator);
        var admin = _api.Authorized(adminTokens.AccessToken);
        var tooMuch = await PostIdempotentAsync(admin, "/api/v1/refunds", new RequestRefundRequest(paid.Payment.Id, 1500m, "Отмена брони", "Cash"));
        (await ProblemCodeAsync(tooMuch)).Should().Be("refund.amount_exceeds_payment");
        var requested = await PostIdempotentAsync(admin, "/api/v1/refunds", new RequestRefundRequest(paid.Payment.Id, 1000m, "Отмена брони", "Cash"));
        requested.StatusCode.Should().Be(HttpStatusCode.Created, await requested.Content.ReadAsStringAsync(Ct));
        var refund = (await requested.Content.ReadFromJsonAsync<RefundDto>(Ct))!;

        // Без права утверждать — нельзя; с правом и лимитом 500 — сумма 1000 больше лимита.
        (await admin.PostAsJsonAsync($"/api/v1/refunds/{refund.Id}/approve", new DecideRefundRequest(null), Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var grant = await owner.PutAsJsonAsync($"/api/v1/staff/{adminStaff.Id}/permissions",
            new SetPermissionsRequest([new PermissionOverrideDto(Permissions.RefundApprove, true, 500m)]), Ct);
        grant.StatusCode.Should().Be(HttpStatusCode.OK, await grant.Content.ReadAsStringAsync(Ct));
        var list = (await admin.GetFromJsonAsync<RefundPageDto>("/api/v1/refunds", Ct))!;
        list.Should().BeEquivalentTo(new { CanApprove = true, ApproveLimit = 500m });
        var limited = await admin.PostAsJsonAsync($"/api/v1/refunds/{refund.Id}/approve", new DecideRefundRequest(null), Ct);
        limited.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ProblemCodeAsync(limited)).Should().Be("refund.limit_exceeded");

        var approved = await owner.PostAsJsonAsync($"/api/v1/refunds/{refund.Id}/approve", new DecideRefundRequest("Согласовано"), Ct);
        approved.StatusCode.Should().Be(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync(Ct));
        (await owner.GetFromJsonAsync<PaymentDto>($"/api/v1/payments/{paid.Payment.Id}", Ct))!.Payment.Status.Should().Be("RefundPending");

        var paidOut = await PostIdempotentAsync(owner, $"/api/v1/refunds/{refund.Id}/mark-paid", new { });
        paidOut.StatusCode.Should().Be(HttpStatusCode.OK, await paidOut.Content.ReadAsStringAsync(Ct));
        var payment = (await owner.GetFromJsonAsync<PaymentDto>($"/api/v1/payments/{paid.Payment.Id}", Ct))!;
        payment.Payment.Status.Should().Be("Refunded");
        payment.Transactions.Should().Contain(t => t.Kind == "Refund" && t.Amount == -1000m);

        var balance = await BalanceAsync(owner, booking.Id);
        balance.Should().BeEquivalentTo(new { Due = 0m, Received = 1000m, Refunded = 1000m, Balance = 0m });
        (await owner.GetFromJsonAsync<DebtListDto>("/api/v1/finance/debts?scope=overpaid", Ct))!.Items.Should().NotContain(d => d.BookingId == booking.Id);
    }

    [Fact]
    public async Task ParallelRefundRequests_NeverExceedPayment()
    {
        var owner = await OwnerAsync();
        var booking = await BookingAsync(owner);
        var paid = (await (await PayAsync(owner, booking.Id, 1000m)).Content.ReadFromJsonAsync<PaymentDto>(Ct))!;

        var responses = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => PostIdempotentAsync(owner, "/api/v1/refunds", new RequestRefundRequest(paid.Payment.Id, 600m, "Тест", "Cash"))));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
    }

    [Fact]
    public async Task LateCancel_WithoutPenalties_LeavesNothingDue()
    {
        var owner = await OwnerAsync();
        var room = await BookingTestKit.CreateRoomAsync(owner, 700m);
        var (date, hour) = BookingTestKit.NextHour(Clock);
        var booking = await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(room.Id, date, hour, 2));
        await owner.PostAsJsonAsync($"/api/v1/bookings/{booking.Id}/cancel", new CancelBookingRequest("Поздняя отмена"), Ct);

        (await BalanceAsync(owner, booking.Id)).Due.Should().Be(0m, "штрафы по умолчанию выключены");
        (await owner.GetFromJsonAsync<DebtListDto>("/api/v1/finance/debts", Ct))!.Items.Should().NotContain(d => d.BookingId == booking.Id);
    }

    [Fact]
    public async Task PaymentsList_TotalsByMethod_ForPeriod()
    {
        var owner = await OwnerAsync();
        var booking = await BookingAsync(owner);
        await PayAsync(owner, booking.Id, 300m, method: "Cash");
        await PayAsync(owner, booking.Id, 200m, method: "MBank");
        var today = Clock.Today;

        var page = (await owner.GetFromJsonAsync<PaymentPageDto>($"/api/v1/payments?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}&bookingId={booking.Id}", Ct))!;

        page.Page.Items.Should().HaveCount(2).And.OnlyContain(p => p.BookingNumber == booking.Number);
        page.Totals.Total.Should().Be(500m);
        page.Totals.ByMethod.Should().Contain(new KeyValuePair<string, decimal>("Cash", 300m)).And.Contain(new KeyValuePair<string, decimal>("MBank", 200m));
    }

    [Fact]
    public async Task GuestCard_ShowsPaidAmount()
    {
        var owner = await OwnerAsync();
        var booking = await BookingAsync(owner);
        await PayAsync(owner, booking.Id, 450m);

        var guest = (await owner.GetFromJsonAsync<Hms.Modules.Guests.Application.GuestDto>($"/api/v1/guests/{booking.GuestId}", Ct))!;
        guest.Stats.PaidAmount.Should().BeGreaterThanOrEqualTo(450m);
    }

    [Fact]
    public async Task Expenses_AdminSeesOwn_OwnerManagesAll_HousekeeperDenied()
    {
        var owner = await OwnerAsync();
        var categories = (await owner.GetFromJsonAsync<List<ExpenseCategoryDto>>("/api/v1/expense-categories", Ct))!;
        categories.Should().Contain(c => c.Name == "Бельё и прачечная");
        var laundry = categories.First(c => c.Name == "Бельё и прачечная");

        var (_, adminTokens) = await _api.CreateActiveStaffAsync(Roles.Administrator);
        var admin = _api.Authorized(adminTokens.AccessToken);
        var created = await PostIdempotentAsync(admin, "/api/v1/expenses",
            new SaveExpenseRequest(laundry.Id, 2400m, Clock.Today, "Cash", "Прачечная «Чистота»", "Стирка постельного белья", null));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var expense = (await created.Content.ReadFromJsonAsync<ExpenseDto>(Ct))!;
        await PostIdempotentAsync(owner, "/api/v1/expenses", new SaveExpenseRequest(laundry.Id, 999m, Clock.Today, "Cash", null, "Расход владельца", null));

        var adminList = (await admin.GetFromJsonAsync<ExpensePageDto>("/api/v1/expenses", Ct))!;
        adminList.CanManage.Should().BeFalse();
        adminList.Page.Items.Should().OnlyContain(e => e.Id == expense.Id);
        (await admin.PostAsJsonAsync($"/api/v1/expenses/{expense.Id}/void", new VoidExpenseRequest("Ошибка"), Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var voided = await owner.PostAsJsonAsync($"/api/v1/expenses/{expense.Id}/void", new VoidExpenseRequest("Внесли дважды"), Ct);
        voided.StatusCode.Should().Be(HttpStatusCode.OK);
        (await voided.Content.ReadFromJsonAsync<ExpenseDto>(Ct))!.IsVoided.Should().BeTrue();
        var ownerList = (await owner.GetFromJsonAsync<ExpensePageDto>("/api/v1/expenses", Ct))!;
        ownerList.CanManage.Should().BeTrue();
        ownerList.Page.Items.Should().NotContain(e => e.Id == expense.Id, "аннулированные скрыты по умолчанию");

        var (_, maidTokens) = await _api.CreateActiveStaffAsync(Roles.Housekeeper);
        var maid = _api.Authorized(maidTokens.AccessToken);
        (await maid.GetAsync(new Uri("/api/v1/expenses", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await maid.GetAsync(new Uri("/api/v1/payments", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await maid.GetAsync(new Uri($"/api/v1/bookings/{Guid.NewGuid()}/balance", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
