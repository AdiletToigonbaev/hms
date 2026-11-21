using System.Net;
using System.Net.Http.Json;
using Hms.IntegrationTests.Bookings;
using Hms.IntegrationTests.Hotel;
using Hms.Modules.Bookings.Application;
using Hms.Modules.Finance.Application;
using Hms.Modules.Hotel.Application;
using Hms.Modules.Housekeeping.Application;
using Hms.Modules.Identity.Contracts;
using Hms.Modules.Rooms.Application;
using Hms.Platform.Time;
using Microsoft.Extensions.DependencyInjection;
using static Hms.IntegrationTests.TestApi;

namespace Hms.IntegrationTests.Housekeeping;

/// <summary>
/// Заселение → выселение → уборка → номер готов (Sprint 7). Меняет настройку «освобождать номер после уборки» —
/// поэтому в группе глобальных настроек.
/// </summary>
[Collection(nameof(GlobalSettingsGroup))]
public class FrontDeskHousekeepingTests(PostgresFixture db)
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

    private static Task<HttpResponseMessage> CheckInAsync(HttpClient client, Guid bookingId) =>
        PostIdempotentAsync(client, $"/api/v1/bookings/{bookingId}/check-in", new { });

    private static Task<HttpResponseMessage> CheckOutAsync(HttpClient client, Guid bookingId, bool allowDebt = false) =>
        PostIdempotentAsync(client, $"/api/v1/bookings/{bookingId}/check-out", new CheckOutRequest(allowDebt));

    private static async Task<string> RoomStatusAsync(HttpClient client, Guid roomId) =>
        (await client.GetFromJsonAsync<RoomDto>($"/api/v1/rooms/{roomId}", Ct))!.Status;

    private static async Task<HttpResponseMessage> PayAsync(HttpClient client, Guid bookingId, decimal amount)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/manual")
        {
            Content = JsonContent.Create(new RegisterPaymentRequest(bookingId, "Stay", "Cash", amount, null)),
        };
        message.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return await client.SendAsync(message, Ct);
    }

    private static async Task SetAutoReleaseAsync(HttpClient owner, bool enabled)
    {
        var settings = (await owner.GetFromJsonAsync<HotelSettingsDto>("/api/v1/hotel/settings", Ct))!;
        var response = await owner.PutAsJsonAsync("/api/v1/hotel/settings", HotelTests.ToUpdate(settings) with { AutoReleaseRoomAfterCleaning = enabled }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    /// <summary>Бронь, которая начинается в ближайший час (заселить уже можно), на 3 часа по 500 сом.</summary>
    private async Task<(BookingDto Booking, Guid RoomId)> SoonBookingAsync(HttpClient owner, bool confirm = true)
    {
        var room = await BookingTestKit.CreateRoomAsync(owner, 500m);
        var (date, hour) = BookingTestKit.NextHour(Clock);
        var booking = await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(room.Id, date, hour, 3, confirm));
        return (booking, room.Id);
    }

    private static async Task<HousekeepingTaskDto> TaskForRoomAsync(HttpClient client, Guid roomId, string? status = null) =>
        (await client.GetFromJsonAsync<List<HousekeepingTaskDto>>($"/api/v1/housekeeping/tasks?roomId={roomId}&status={status}", Ct))!.Single();

    private static async Task<HousekeepingTaskDto> ReadAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<HousekeepingTaskDto>(Ct))!;
    }

    /// <summary>Уборщик берёт задачу, отмечает чек-лист и завершает уборку.</summary>
    private static async Task CleanAsync(HttpClient maid, HousekeepingTaskDto task)
    {
        (await ReadAsync(await maid.PostAsync(new Uri($"/api/v1/housekeeping/tasks/{task.Id}/take", UriKind.Relative), null, Ct))).Status.Should().Be("Assigned");
        (await ReadAsync(await maid.PostAsync(new Uri($"/api/v1/housekeeping/tasks/{task.Id}/start", UriKind.Relative), null, Ct))).Status.Should().Be("InProgress");
        var early = await maid.PostAsync(new Uri($"/api/v1/housekeeping/tasks/{task.Id}/complete", UriKind.Relative), null, Ct);
        (await ProblemCodeAsync(early)).Should().Be("housekeeping.checklist_incomplete");
        foreach (var item in task.Checklist)
        {
            await ReadAsync(await maid.PutAsJsonAsync($"/api/v1/housekeeping/tasks/{task.Id}/checklist/{item.Id}", new ChecklistItemRequest(true), Ct));
        }

        (await ReadAsync(await maid.PostAsync(new Uri($"/api/v1/housekeeping/tasks/{task.Id}/complete", UriKind.Relative), null, Ct))).Status
            .Should().BeOneOf("Completed", "Inspected");
    }

    [Fact]
    public async Task FullFlow_CheckIn_CheckOut_Cleaning_Inspection_RoomReady()
    {
        var owner = await OwnerAsync();
        await SetAutoReleaseAsync(owner, false);
        var (booking, roomId) = await SoonBookingAsync(owner);
        booking.AllowedActions.Should().Contain("checkIn");

        var preview = (await owner.GetFromJsonAsync<CheckInPreviewDto>($"/api/v1/bookings/{booking.Id}/check-in/preview", Ct))!;
        preview.Allowed.Should().BeTrue(preview.BlockMessage);
        preview.Early.Should().BeTrue();

        var checkedIn = await CheckInAsync(owner, booking.Id);
        checkedIn.StatusCode.Should().Be(HttpStatusCode.OK, await checkedIn.Content.ReadAsStringAsync(Ct));
        var inRoom = (await checkedIn.Content.ReadFromJsonAsync<BookingDto>(Ct))!;
        inRoom.Status.Should().Be("CheckedIn");
        inRoom.ActualCheckInAt.Should().NotBeNull();
        inRoom.AllowedActions.Should().Contain("checkOut");
        (await RoomStatusAsync(owner, roomId)).Should().Be("Occupied");

        // Долг: без оплаты выселить нельзя, пока администратор не подтвердит долг.
        var debt = await CheckOutAsync(owner, booking.Id);
        (await ProblemCodeAsync(debt)).Should().Be("checkout.debt");
        (await PayAsync(owner, booking.Id, 1500m)).StatusCode.Should().Be(HttpStatusCode.Created);

        var outPreview = (await owner.GetFromJsonAsync<CheckOutPreviewDto>($"/api/v1/bookings/{booking.Id}/check-out/preview", Ct))!;
        outPreview.Early.Should().BeTrue();
        outPreview.Credit.Should().Be(0m, "по умолчанию неиспользованные часы не возвращаются");

        var checkedOut = await CheckOutAsync(owner, booking.Id);
        checkedOut.StatusCode.Should().Be(HttpStatusCode.OK, await checkedOut.Content.ReadAsStringAsync(Ct));
        var left = (await checkedOut.Content.ReadFromJsonAsync<BookingDto>(Ct))!;
        left.Status.Should().Be("CheckedOut");
        left.ActualCheckOutAt.Should().NotBeNull();
        (await RoomStatusAsync(owner, roomId)).Should().Be("Cleaning");

        var task = await TaskForRoomAsync(owner, roomId);
        task.Origin.Should().Be("CheckOut");
        task.DueAt.Should().NotBeNull();
        task.Checklist.Should().NotBeEmpty();

        var (_, maidTokens) = await _api.CreateActiveStaffAsync(Roles.Housekeeper);
        var maid = _api.Authorized(maidTokens.AccessToken);
        var mine = (await maid.GetFromJsonAsync<List<HousekeepingTaskDto>>("/api/v1/housekeeping/my/tasks", Ct))!;
        mine.Should().Contain(t => t.Id == task.Id && t.AllowedActions.Contains("take"));

        await CleanAsync(maid, task);
        (await RoomStatusAsync(owner, roomId)).Should().Be("Cleaning", "без настройки владельца номер ждёт проверки");

        var rejected = await ReadAsync(await owner.PostAsJsonAsync($"/api/v1/housekeeping/tasks/{task.Id}/inspect", new InspectTaskRequest(false, "Пыль на полке"), Ct));
        rejected.Status.Should().Be("Rejected");
        rejected.Checklist.Should().OnlyContain(i => !i.IsDone);

        (await ReadAsync(await maid.PostAsync(new Uri($"/api/v1/housekeeping/tasks/{task.Id}/start", UriKind.Relative), null, Ct))).Status.Should().Be("InProgress");
        foreach (var item in rejected.Checklist)
        {
            await ReadAsync(await maid.PutAsJsonAsync($"/api/v1/housekeeping/tasks/{task.Id}/checklist/{item.Id}", new ChecklistItemRequest(true), Ct));
        }

        await ReadAsync(await maid.PostAsync(new Uri($"/api/v1/housekeeping/tasks/{task.Id}/complete", UriKind.Relative), null, Ct));
        var accepted = await ReadAsync(await owner.PostAsJsonAsync($"/api/v1/housekeeping/tasks/{task.Id}/inspect", new InspectTaskRequest(true, null), Ct));
        accepted.Status.Should().Be("Inspected");
        (await RoomStatusAsync(owner, roomId)).Should().Be("Available");

        var history = (await maid.GetFromJsonAsync<List<HousekeepingTaskDto>>("/api/v1/housekeeping/my/history", Ct))!;
        history.Should().Contain(t => t.Id == task.Id);
    }

    [Fact]
    public async Task AutoRelease_RoomAvailableRightAfterCleaning()
    {
        var owner = await OwnerAsync();
        await SetAutoReleaseAsync(owner, true);
        try
        {
            var (booking, roomId) = await SoonBookingAsync(owner);
            (await CheckInAsync(owner, booking.Id)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await CheckOutAsync(owner, booking.Id, allowDebt: true)).StatusCode.Should().Be(HttpStatusCode.OK);

            var (_, maidTokens) = await _api.CreateActiveStaffAsync(Roles.Housekeeper);
            var maid = _api.Authorized(maidTokens.AccessToken);
            await CleanAsync(maid, await TaskForRoomAsync(owner, roomId));

            (await RoomStatusAsync(owner, roomId)).Should().Be("Available");
            (await TaskForRoomAsync(owner, roomId, "Inspected")).Status.Should().Be("Inspected");
        }
        finally
        {
            await SetAutoReleaseAsync(owner, false);
        }
    }

    [Fact]
    public async Task CheckIn_Blocked_WhenNotConfirmed_TooEarly_OrRoomNotReady()
    {
        var owner = await OwnerAsync();
        var (pending, _) = await SoonBookingAsync(owner, confirm: false);
        (await ProblemCodeAsync(await CheckInAsync(owner, pending.Id))).Should().Be("booking.checkin_not_confirmed");

        var room = await BookingTestKit.CreateRoomAsync(owner, 500m);
        var far = await BookingTestKit.CreateAsync(owner, BookingTestKit.Hourly(room.Id, Clock.Today.AddDays(5), 10, 2));
        (await ProblemCodeAsync(await CheckInAsync(owner, far.Id))).Should().Be("booking.checkin_too_early");

        var (soon, roomId) = await SoonBookingAsync(owner);
        var changed = await owner.PostAsJsonAsync($"/api/v1/rooms/{roomId}/status", new { Status = "Maintenance", Reason = "Течёт кран" }, Ct);
        changed.IsSuccessStatusCode.Should().BeTrue(await changed.Content.ReadAsStringAsync(Ct));
        var preview = (await owner.GetFromJsonAsync<CheckInPreviewDto>($"/api/v1/bookings/{soon.Id}/check-in/preview", Ct))!;
        preview.Allowed.Should().BeFalse();
        preview.BlockCode.Should().Be("room.unavailable");
    }

    [Fact]
    public async Task CheckOut_NotCheckedIn_IsRejected_AndCheckInIsIdempotent()
    {
        var owner = await OwnerAsync();
        var (booking, _) = await SoonBookingAsync(owner);
        (await ProblemCodeAsync(await CheckOutAsync(owner, booking.Id))).Should().Be("booking.not_checked_in");

        var key = Guid.NewGuid().ToString("N");
        async Task<HttpResponseMessage> Once()
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/bookings/{booking.Id}/check-in") { Content = JsonContent.Create(new { }) };
            message.Headers.Add("Idempotency-Key", key);
            return await owner.SendAsync(message, Ct);
        }

        (await Once()).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Once()).StatusCode.Should().Be(HttpStatusCode.OK, "повтор с тем же ключом возвращает сохранённый ответ");
    }

    [Fact]
    public async Task Housekeeper_CannotCheckIn_NorSeeOthersTasks_NorManage()
    {
        var owner = await OwnerAsync();
        var (booking, roomId) = await SoonBookingAsync(owner);
        var (_, maidTokens) = await _api.CreateActiveStaffAsync(Roles.Housekeeper);
        var maid = _api.Authorized(maidTokens.AccessToken);

        (await CheckInAsync(maid, booking.Id)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await maid.GetAsync(new Uri("/api/v1/housekeeping/tasks", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await maid.PostAsJsonAsync("/api/v1/housekeeping/tasks", new CreateTaskRequest(roomId, "Normal", null, null), Ct)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        // Задача другого уборщика: не видна и не берётся.
        var created = await owner.PostAsJsonAsync("/api/v1/housekeeping/tasks", new CreateTaskRequest(roomId, "High", "Генеральная уборка", null), Ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var task = (await created.Content.ReadFromJsonAsync<HousekeepingTaskDto>(Ct))!;
        var (other, _) = await _api.CreateActiveStaffAsync(Roles.Housekeeper);
        await ReadAsync(await owner.PostAsJsonAsync($"/api/v1/housekeeping/tasks/{task.Id}/assign", new AssignTaskRequest(other.Id), Ct));

        (await maid.GetAsync(new Uri($"/api/v1/housekeeping/tasks/{task.Id}", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await maid.PostAsync(new Uri($"/api/v1/housekeeping/tasks/{task.Id}/start", UriKind.Relative), null, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await maid.GetFromJsonAsync<List<HousekeepingTaskDto>>("/api/v1/housekeeping/my/tasks", Ct))!.Should().NotContain(t => t.Id == task.Id);

        // Назначить можно только уборщика.
        var (admin, _) = await _api.CreateActiveStaffAsync(Roles.Administrator);
        (await ProblemCodeAsync(await owner.PostAsJsonAsync($"/api/v1/housekeeping/tasks/{task.Id}/assign", new AssignTaskRequest(admin.Id), Ct)))
            .Should().Be("housekeeping.not_housekeeper");
    }

    [Fact]
    public async Task Maintenance_HousekeeperReports_AdminClosesRoom()
    {
        var owner = await OwnerAsync();
        var room = await BookingTestKit.CreateRoomAsync(owner, 500m);
        var (_, maidTokens) = await _api.CreateActiveStaffAsync(Roles.Housekeeper);
        var maid = _api.Authorized(maidTokens.AccessToken);

        var reported = await PostIdempotentAsync(maid, "/api/v1/maintenance-requests", new CreateMaintenanceRequest(room.Id, null, "Defect", "High", "Не работает кондиционер"));
        reported.StatusCode.Should().Be(HttpStatusCode.Created, await reported.Content.ReadAsStringAsync(Ct));
        var request = (await reported.Content.ReadFromJsonAsync<MaintenanceRequestDto>(Ct))!;
        (await maid.GetFromJsonAsync<List<MaintenanceRequestDto>>("/api/v1/housekeeping/my/maintenance-requests", Ct))!.Should().Contain(r => r.Id == request.Id);
        (await maid.GetAsync(new Uri("/api/v1/maintenance-requests", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var updated = await owner.PutAsJsonAsync($"/api/v1/maintenance-requests/{request.Id}", new UpdateMaintenanceRequest("InProgress", "Вызвали мастера", true, null), Ct);
        updated.StatusCode.Should().Be(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync(Ct));
        (await RoomStatusAsync(owner, room.Id)).Should().Be("Maintenance");

        var resolved = await owner.PutAsJsonAsync($"/api/v1/maintenance-requests/{request.Id}", new UpdateMaintenanceRequest("Resolved", "Заменили фильтр", false, null), Ct);
        (await resolved.Content.ReadFromJsonAsync<MaintenanceRequestDto>(Ct))!.Status.Should().Be("Resolved");
        var closed = await owner.PutAsJsonAsync($"/api/v1/maintenance-requests/{request.Id}", new UpdateMaintenanceRequest("InProgress", null, false, null), Ct);
        (await ProblemCodeAsync(closed)).Should().Be("maintenance.closed");
    }

    [Fact]
    public async Task ChecklistTemplate_Edit_AppliesToNewTasks()
    {
        var owner = await OwnerAsync();
        var template = (await owner.GetFromJsonAsync<ChecklistTemplateDto>("/api/v1/housekeeping/checklist-template", Ct))!;
        template.Items.Should().NotBeEmpty();
        try
        {
            var saved = await owner.PutAsJsonAsync("/api/v1/housekeeping/checklist-template",
                new SaveChecklistTemplateRequest([.. template.Items, "Проверить сейф"], template.Version), Ct);
            saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(Ct));

            var stale = await owner.PutAsJsonAsync("/api/v1/housekeeping/checklist-template", new SaveChecklistTemplateRequest(["Один пункт"], template.Version), Ct);
            stale.StatusCode.Should().Be(HttpStatusCode.Conflict);

            var room = await BookingTestKit.CreateRoomAsync(owner, 500m);
            var created = await owner.PostAsJsonAsync("/api/v1/housekeeping/tasks", new CreateTaskRequest(room.Id, "Normal", null, null), Ct);
            (await created.Content.ReadFromJsonAsync<HousekeepingTaskDto>(Ct))!.Checklist.Select(i => i.Text).Should().Contain("Проверить сейф");
        }
        finally
        {
            var current = (await owner.GetFromJsonAsync<ChecklistTemplateDto>("/api/v1/housekeeping/checklist-template", Ct))!;
            await owner.PutAsJsonAsync("/api/v1/housekeeping/checklist-template", new SaveChecklistTemplateRequest(template.Items, current.Version), Ct);
        }
    }
}
