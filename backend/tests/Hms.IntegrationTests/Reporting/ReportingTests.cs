using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hms.IntegrationTests.Bookings;
using Hms.Modules.Bookings.Application;
using Hms.Modules.Finance.Application;
using Hms.Modules.Identity.Contracts;
using Hms.Modules.Reporting.Application;
using Hms.Modules.Rooms.Application;
using Hms.Platform.Time;
using Microsoft.Extensions.DependencyInjection;
using static Hms.IntegrationTests.TestApi;

namespace Hms.IntegrationTests.Reporting;

/// <summary>
/// Отчёты и дашборды (Sprint 9): сверка показателей с эталонными бронями и платежами, выгрузка Excel/PDF, права.
/// Данные теста изолированы фильтром по своей категории номера.
/// </summary>
public class ReportingTests(PostgresFixture db)
{
    private readonly TestApi _api = new(db);

    private IHotelClock Clock => db.Factory.Services.GetRequiredService<IHotelClock>();

    private async Task<HttpClient> OwnerAsync() => _api.Authorized((await _api.LoginOwnerAsync()).AccessToken);

    private static async Task<HttpResponseMessage> PostIdempotentAsync<T>(HttpClient client, string url, T body)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        message.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return await client.SendAsync(message, Ct);
    }

    /// <summary>
    /// Эталон: категория (500 за час, 3000 за ночь), 2 номера. Почасовая 2 ч = 1000 (оплачена 1000 через MBANK),
    /// почасовая 3 ч = 1500 — отменена без штрафа, посуточная 2 ночи = 6000.
    /// </summary>
    private async Task<(HttpClient Owner, Guid TypeId, string Query)> SeedAsync()
    {
        var owner = await OwnerAsync();
        var room = await BookingTestKit.CreateRoomAsync(owner, 500m);
        var second = await owner.PostAsJsonAsync("/api/v1/rooms", new SaveRoomRequest(room.RoomTypeId, BookingTestKit.UniqueNumber(), 1, null, null, null, null, null, null), Ct);
        second.StatusCode.Should().Be(HttpStatusCode.Created);

        var day = Clock.Today.AddDays(30);
        var paid = await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(room.Id, day, 12, 2));
        var cancelled = await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(room.Id, day, 15, 3));
        (await owner.PostAsJsonAsync($"/api/v1/bookings/{cancelled.Id}/cancel", new CancelBookingRequest("Тест отчёта"), Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        await BookingTestKit.CreateAsync(owner, new CreateBookingRequest(room.Id, day.AddDays(1), day.AddDays(3), 2, 0, "Гость Отчёта", "+996555123456", null, null, null, null, true));
        (await PostIdempotentAsync(owner, "/api/v1/payments/manual", new RegisterPaymentRequest(paid.Id, "Prepayment", "MBank", 1000m, null))).StatusCode
            .Should().Be(HttpStatusCode.Created);

        var query = $"from={Clock.Today:yyyy-MM-dd}&to={Clock.Today.AddDays(40):yyyy-MM-dd}&roomTypeId={room.RoomTypeId}";
        return (owner, room.RoomTypeId, query);
    }

    [Fact]
    public async Task Financial_MatchesReferenceData()
    {
        var (owner, _, query) = await SeedAsync();

        var response = await owner.GetAsync(new Uri($"/api/v1/reports/financial?{query}", UriKind.Relative), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var report = (await response.Content.ReadFromJsonAsync<ReportTableView>(Ct))!;
        decimal? Value(string label) => report.Summary.Single(s => s.Label == label).Value;

        Value("Начисленная выручка").Should().Be(7000m, "1000 почасовая + 6000 посуточная; отменённая без штрафа не в счёт");
        Value("Поступления").Should().Be(1000m);
        Value("Возвраты").Should().Be(0m);
        Value("Брони").Should().Be(3m);
        Value("Отмены").Should().Be(1m);
        Value("ADR (средняя цена ночи)").Should().Be(3000m);
        Value("Средняя цена часа").Should().Be(500m);
        Value("Загрузка").Should().BeGreaterThan(0m);
        report.Rows.Should().HaveCount(41, "строка на каждый день периода");
        report.Note.Should().NotBeNull("с фильтром по категории расходы не учитываются");

        var dashboard = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/dashboard/owner?{query}", Ct);
        dashboard.GetProperty("kpi").GetProperty("accruedRevenue").GetDecimal().Should().Be(7000m);
        dashboard.GetProperty("availableRooms").GetInt32().Should().Be(2);
        dashboard.GetProperty("daily").GetArrayLength().Should().Be(41);
    }

    [Fact]
    public async Task Financial_ByHoursOfDay()
    {
        var (owner, typeId, _) = await SeedAsync();
        var day = Clock.Today.AddDays(30).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        async Task<decimal?> AccruedAsync(string hours)
        {
            var r = await owner.GetAsync(new Uri($"/api/v1/reports/financial?period=day&date={day}&roomTypeId={typeId}{hours}", UriKind.Relative), Ct);
            r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync(Ct));
            return (await r.Content.ReadFromJsonAsync<ReportTableView>(Ct))!.Summary.Single(x => x.Label == "Начисленная выручка").Value;
        }

        // Почасовая бронь 12:00–14:00 = 1000; посуточная начинается со следующего дня.
        (await AccruedAsync("")).Should().Be(1000m);
        (await AccruedAsync("&fromHour=12&toHour=14")).Should().Be(1000m);
        (await AccruedAsync("&fromHour=13&toHour=22")).Should().Be(500m, "в окно попал один час из двух");
        (await AccruedAsync("&fromHour=14")).Should().Be(0m);

        var bad = await owner.GetAsync(new Uri($"/api/v1/reports/financial?period=day&date={day}&fromHour=22&toHour=6", UriKind.Relative), Ct);
        (await ProblemCodeAsync(bad)).Should().Be("report.hours_invalid");
        var dash = await owner.GetAsync(new Uri($"/api/v1/dashboard/owner?period=day&date={day}&fromHour=6&toHour=22", UriKind.Relative), Ct);
        dash.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reports_FilterByStatusMethodAndStaff()
    {
        var (owner, _, query) = await SeedAsync();

        var cancelled = (await owner.GetFromJsonAsync<ReportTableView>($"/api/v1/reports/bookings?{query}&bookingStatus=Cancelled", Ct))!;
        cancelled.Rows.Should().ContainSingle();
        cancelled.Filters.Should().Contain(f => f.StartsWith("Статус брони", StringComparison.Ordinal));

        var mbank = (await owner.GetFromJsonAsync<ReportTableView>($"/api/v1/reports/payments?{query}&paymentMethod=MBank", Ct))!;
        mbank.Rows.Should().ContainSingle();
        var cash = (await owner.GetFromJsonAsync<ReportTableView>($"/api/v1/reports/payments?{query}&paymentMethod=Cash", Ct))!;
        cash.Rows.Should().BeEmpty();

        var me = await owner.GetFromJsonAsync<JsonElement>("/api/v1/me", Ct);
        var staff = (await owner.GetFromJsonAsync<ReportTableView>($"/api/v1/reports/staff?{query}&staffId={me.GetProperty("id").GetString()}", Ct))!;
        staff.Rows.Should().ContainSingle();
        staff.Rows[0][2].GetInt32().Should().BeGreaterThanOrEqualTo(3, "владелец создал три брони");

        foreach (var type in new[] { "guests", "rooms", "refunds", "expenses" })
        {
            var r = await owner.GetAsync(new Uri($"/api/v1/reports/{type}?{query}", UriKind.Relative), Ct);
            r.StatusCode.Should().Be(HttpStatusCode.OK, $"{type}: {await r.Content.ReadAsStringAsync(Ct)}");
        }

        var rooms = (await owner.GetFromJsonAsync<ReportTableView>($"/api/v1/reports/rooms?{query}", Ct))!;
        rooms.Rows.Should().HaveCount(2);
    }

    [Fact]
    public async Task Export_ExcelAndPdf()
    {
        var (owner, _, query) = await SeedAsync();

        var xlsx = await owner.GetAsync(new Uri($"/api/v1/reports/financial/export?format=xlsx&{query}", UriKind.Relative), Ct);
        xlsx.StatusCode.Should().Be(HttpStatusCode.OK, await xlsx.Content.ReadAsStringAsync(Ct));
        xlsx.Content.Headers.ContentType!.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        var xlsxBytes = await xlsx.Content.ReadAsByteArrayAsync(Ct);
        xlsxBytes.Take(2).Should().Equal((byte)'P', (byte)'K');

        foreach (var type in new[] { "financial", "bookings", "staff" })
        {
            var pdf = await owner.GetAsync(new Uri($"/api/v1/reports/{type}/export?format=pdf&{query}", UriKind.Relative), Ct);
            pdf.StatusCode.Should().Be(HttpStatusCode.OK, await pdf.Content.ReadAsStringAsync(Ct));
            pdf.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
            System.Text.Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync(Ct)).Take(5).ToArray()).Should().Be("%PDF-");
        }

        (await owner.GetAsync(new Uri($"/api/v1/reports/financial/export?format=doc&{query}", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await owner.GetAsync(new Uri($"/api/v1/reports/unknown?{query}", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.GetAsync(new Uri("/api/v1/reports/financial?from=2026-12-10&to=2026-12-01", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await owner.GetAsync(new Uri("/api/v1/reports/financial?from=2025-01-01&to=2026-12-31", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Permissions_AdminSeesOperationsOnly_HousekeeperNothing()
    {
        var (_, adminTokens) = await _api.CreateActiveStaffAsync(Roles.Administrator);
        var admin = _api.Authorized(adminTokens.AccessToken);
        (await admin.GetAsync(new Uri("/api/v1/dashboard/operations", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync(new Uri("/api/v1/dashboard/owner", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.GetAsync(new Uri("/api/v1/reports/financial", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.GetAsync(new Uri("/api/v1/reports/financial/export?format=pdf", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var (_, maidTokens) = await _api.CreateActiveStaffAsync(Roles.Housekeeper);
        var maid = _api.Authorized(maidTokens.AccessToken);
        foreach (var url in new[] { "/api/v1/dashboard/operations", "/api/v1/dashboard/owner", "/api/v1/reports/payments", "/api/v1/reports/staff" })
        {
            (await maid.GetAsync(new Uri(url, UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden, url);
        }
    }

    [Fact]
    public async Task OperationsDashboard_ShowsTodayArrivals()
    {
        var owner = await OwnerAsync();
        var room = await BookingTestKit.CreateRoomAsync(owner, 500m);
        var (date, hour) = BookingTestKit.NextHour(Clock);
        var booking = await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(room.Id, date, hour, 2));

        var ops = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/dashboard/operations?date={date:yyyy-MM-dd}", Ct);
        ops.GetProperty("arrivals").EnumerateArray().Should().Contain(a => a.GetProperty("bookingId").GetGuid() == booking.Id);
        ops.GetProperty("roomsByStatus").EnumerateObject().Should().NotBeEmpty();
    }

    /// <summary>Отчёт как его видит клиент: ячейки — JSON-значения.</summary>
    private sealed record ReportTableView(string Type, string Title, List<string> Filters, List<ReportSummaryItem> Summary, List<ReportColumn> Columns, List<List<JsonElement>> Rows, string? Note);
}
