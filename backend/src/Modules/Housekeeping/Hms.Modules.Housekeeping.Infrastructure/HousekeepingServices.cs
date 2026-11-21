using System.Globalization;
using Hms.Modules.Audit.Contracts;
using Hms.Modules.Bookings.Contracts;
using Hms.Modules.Hotel.Contracts;
using Hms.Modules.Housekeeping.Application;
using Hms.Modules.Housekeeping.Contracts;
using Hms.Modules.Housekeeping.Domain;
using Hms.Modules.Identity.Contracts;
using Hms.Modules.Rooms.Contracts;
using Hms.Platform.Security;
using Hms.Platform.Time;
using Hms.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Hms.Modules.Housekeeping.Infrastructure;

/// <summary>Задача после выезда: срок — время уборки номера; высокий приоритет, если сегодня скоро заезд.</summary>
internal sealed class HousekeepingTasks(DbContext db, IHotelContext hotel, IRoomBookingQueries bookings, IHotelClock clock, IAuditTrail audit) : IHousekeepingTasks
{
    public async Task<Guid> CreateAfterCheckOutAsync(Guid roomId, Guid bookingId, DateTimeOffset dueAt, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var now = clock.UtcNow;
        var states = await bookings.TodayStatesAsync(now, clock.ToUtc(clock.Today.AddDays(1), TimeOnly.MinValue), ct);
        var nextArrival = states.GetValueOrDefault(roomId)?.NextStartsAt;
        var priority = nextArrival is { } next && next - now < TimeSpan.FromHours(3) ? TaskPriority.High : TaskPriority.Normal;
        var template = await db.Set<ChecklistTemplate>().AsNoTracking().FirstOrDefaultAsync(t => t.HotelId == hotelId && t.IsDefault, ct);
        var task = HousekeepingTask.Create(hotelId, roomId, bookingId, TaskOrigin.CheckOut, priority, dueAt, null,
            template?.Items ?? [.. ChecklistTemplateSeeder.DefaultItems], null, now);
        db.Set<HousekeepingTask>().Add(task);
        audit.Record(new AuditEntry("housekeeping.task_created", "HousekeepingTask", task.Id.ToString(), After: new { roomId, bookingId, Auto = true }));
        return task.Id;
    }
}

internal sealed class HousekeepingService(
    DbContext db,
    IHotelContext hotel,
    IRoomDirectory rooms,
    IRoomStatusChanger roomStatus,
    IRoomBookingQueries bookings,
    IUserDirectory users,
    ICurrentUser currentUser,
    IUserAccessProvider access,
    IAuditTrail audit,
    IHotelClock clock) : IHousekeepingService
{
    private static readonly HousekeepingTaskStatus[] OpenStatuses =
        [HousekeepingTaskStatus.Pending, HousekeepingTaskStatus.Assigned, HousekeepingTaskStatus.InProgress, HousekeepingTaskStatus.Completed, HousekeepingTaskStatus.Rejected];

    public async Task<IReadOnlyList<HousekeepingTaskDto>> ListAsync(TaskQuery q, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var query = db.Set<HousekeepingTask>().AsNoTracking().Include(t => t.Items).Where(t => t.HotelId == hotelId);
        if (q.Status == "done")
        {
            var since = clock.UtcNow.AddDays(-7);
            query = query.Where(t => t.Status == HousekeepingTaskStatus.Inspected && t.InspectedAt >= since);
        }
        else if (Enum.TryParse<HousekeepingTaskStatus>(q.Status, out var status))
        {
            query = query.Where(t => t.Status == status);
        }
        else
        {
            query = query.Where(t => OpenStatuses.Contains(t.Status));
        }

        if (q.RoomId is { } roomId)
        {
            query = query.Where(t => t.RoomId == roomId);
        }

        if (q.AssignedTo is { } assignee)
        {
            query = query.Where(t => t.AssignedTo == assignee);
        }

        var tasks = await query.OrderByDescending(t => t.Priority).ThenBy(t => t.DueAt).ThenBy(t => t.CreatedAt).ToListAsync(ct);
        return await ToDtosAsync(tasks, ct);
    }

    public async Task<IReadOnlyList<HousekeepingTaskDto>> MyTasksAsync(CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var me = currentUser.UserId;
        var tasks = await db.Set<HousekeepingTask>().AsNoTracking().Include(t => t.Items)
            .Where(t => t.HotelId == hotelId && OpenStatuses.Contains(t.Status)
                && (t.AssignedTo == me || (t.AssignedTo == null && t.Status == HousekeepingTaskStatus.Pending)))
            .OrderByDescending(t => t.Priority).ThenBy(t => t.DueAt).ThenBy(t => t.CreatedAt)
            .ToListAsync(ct);
        return await ToDtosAsync(tasks, ct);
    }

    public async Task<IReadOnlyList<HousekeepingTaskDto>> MyHistoryAsync(CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var me = currentUser.UserId;
        var tasks = await db.Set<HousekeepingTask>().AsNoTracking().Include(t => t.Items)
            .Where(t => t.HotelId == hotelId && t.AssignedTo == me && t.Status == HousekeepingTaskStatus.Inspected)
            .OrderByDescending(t => t.InspectedAt).Take(50)
            .ToListAsync(ct);
        return await ToDtosAsync(tasks, ct);
    }

    public async Task<Result<HousekeepingTaskDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var task = await LoadAsync(id, tracking: false, ct);
        if (task is null || !await CanSeeAsync(task, ct))
        {
            return HousekeepingErrors.NotFound;
        }

        return (await ToDtosAsync([task], ct))[0];
    }

    public async Task<Result<HousekeepingTaskDto>> CreateAsync(CreateTaskRequest r, CancellationToken ct)
    {
        var room = await rooms.GetAsync(r.RoomId, ct);
        if (room is null || room.IsArchived)
        {
            return Error.NotFound("room.not_found", "Номер не найден.");
        }

        var hotelId = await hotel.GetHotelIdAsync(ct);
        var template = await db.Set<ChecklistTemplate>().AsNoTracking().FirstOrDefaultAsync(t => t.HotelId == hotelId && t.IsDefault, ct);
        var task = HousekeepingTask.Create(hotelId, room.Id, null, TaskOrigin.Manual, Enum.Parse<TaskPriority>(r.Priority), r.DueAt, r.Notes,
            template?.Items ?? [.. ChecklistTemplateSeeder.DefaultItems], currentUser.UserId, clock.UtcNow);
        db.Set<HousekeepingTask>().Add(task);
        audit.Record(new AuditEntry("housekeeping.task_created", "HousekeepingTask", task.Id.ToString(), After: new { room = room.Number, Auto = false }));
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([task], ct))[0];
    }

    public Task<Result<HousekeepingTaskDto>> UpdateAsync(Guid id, UpdateTaskRequest r, CancellationToken ct) =>
        ChangeAsync(id, r.Version, t => t.Update(Enum.Parse<TaskPriority>(r.Priority), r.Notes, r.DueAt), "housekeeping.task_updated", null, ct);

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct)
    {
        var task = await LoadAsync(id, tracking: true, ct);
        if (task is null)
        {
            return HousekeepingErrors.NotFound;
        }

        var result = task.Delete();
        if (result.IsFailure)
        {
            return result.Error;
        }

        audit.Record(new AuditEntry("housekeeping.task_deleted", "HousekeepingTask", id.ToString()));
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<HousekeepingTaskDto>> AssignAsync(Guid id, AssignTaskRequest r, CancellationToken ct)
    {
        var staff = await users.ListActiveInRoleAsync(Roles.Housekeeper, ct);
        if (!staff.ContainsKey(r.UserId))
        {
            return Error.Validation("housekeeping.not_housekeeper", "Назначить можно только активного уборщика.");
        }

        return await ChangeAsync(id, null, t => t.Assign(r.UserId), "housekeeping.task_assigned", null, ct);
    }

    public Task<Result<HousekeepingTaskDto>> TakeAsync(Guid id, CancellationToken ct) =>
        ChangeAsync(id, null, t => t.Take(Me), "housekeeping.task_taken", null, ct);

    public Task<Result<HousekeepingTaskDto>> StartAsync(Guid id, CancellationToken ct) =>
        ChangeAsync(id, null, t => t.Start(Me, clock.UtcNow), "housekeeping.task_started", null, ct);

    public Task<Result<HousekeepingTaskDto>> SetItemAsync(Guid id, Guid itemId, ChecklistItemRequest r, CancellationToken ct) =>
        ChangeAsync(id, null, t => t.SetItem(itemId, r.Done, Me, clock.UtcNow), null, null, ct);

    /// <summary>Уборка завершена: если владелец разрешил освобождать номер без проверки — номер сразу «Свободен».</summary>
    public Task<Result<HousekeepingTaskDto>> CompleteAsync(Guid id, CancellationToken ct) =>
        ChangeAsync(id, null, t => t.Complete(Me, clock.UtcNow), "housekeeping.task_completed", async t =>
        {
            var settings = await hotel.GetSettingsAsync(ct);
            if (!settings.AutoReleaseRoomAfterCleaning)
            {
                return Result.Success();
            }

            t.AutoAccept(clock.UtcNow);
            return await ReleaseRoomAsync(t, ct);
        }, ct);

    /// <summary>Проверка: принято — номер «Свободен»; не принято — уборщик убирает заново.</summary>
    public Task<Result<HousekeepingTaskDto>> InspectAsync(Guid id, InspectTaskRequest r, CancellationToken ct) =>
        ChangeAsync(id, null, t => t.Inspect(r.Approved, r.Reason, currentUser.UserId, clock.UtcNow), r.Approved ? "housekeeping.task_accepted" : "housekeeping.task_rejected",
            t => r.Approved ? ReleaseRoomAsync(t, ct) : Task.FromResult(Result.Success()), ct);

    public async Task<IReadOnlyList<StaffMemberDto>> StaffAsync(CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var staff = await users.ListActiveInRoleAsync(Roles.Housekeeper, ct);
        var ids = staff.Keys.ToList();
        var open = await db.Set<HousekeepingTask>().AsNoTracking()
            .Where(t => t.HotelId == hotelId && t.AssignedTo != null && ids.Contains(t.AssignedTo.Value) && OpenStatuses.Contains(t.Status))
            .GroupBy(t => t.AssignedTo!.Value).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return staff.Select(s => new StaffMemberDto(s.Key, s.Value, open.GetValueOrDefault(s.Key))).ToList();
    }

    public async Task<ChecklistTemplateDto> GetTemplateAsync(CancellationToken ct)
    {
        var template = await TemplateAsync(tracking: false, ct);
        return new ChecklistTemplateDto(template.Id, template.Name, template.Items, template.Version);
    }

    public async Task<Result<ChecklistTemplateDto>> SaveTemplateAsync(SaveChecklistTemplateRequest r, CancellationToken ct)
    {
        var template = await TemplateAsync(tracking: true, ct);
        if (r.Version is { } version)
        {
            db.Entry(template).Property(t => t.Version).OriginalValue = version;
        }

        var before = template.Items.ToList();
        template.SetItems(r.Items);
        audit.Record(new AuditEntry("housekeeping.checklist_updated", "ChecklistTemplate", template.Id.ToString(), before, template.Items));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return Conflict;
        }

        return new ChecklistTemplateDto(template.Id, template.Name, template.Items, template.Version);
    }

    private static readonly Error Conflict = Error.Conflict("concurrency.conflict", "Данные изменил другой пользователь. Обновите страницу и повторите.");

    private Guid Me => currentUser.UserId ?? Guid.Empty;

    /// <summary>Номер был на уборке — теперь свободен (источник Housekeeping). Если статус номера уже другой — не трогаем.</summary>
    private async Task<Result> ReleaseRoomAsync(HousekeepingTask task, CancellationToken ct) =>
        await roomStatus.GetStatusAsync(task.RoomId, ct) == RoomStatus.Cleaning
            ? await roomStatus.ChangeAsync(task.RoomId, RoomStatus.Available, RoomStatusSource.Housekeeping, null, task.BookingId, ct)
            : Result.Success();

    private async Task<Result<HousekeepingTaskDto>> ChangeAsync(
        Guid id, uint? version, Func<HousekeepingTask, Result> change, string? action, Func<HousekeepingTask, Task<Result>>? after, CancellationToken ct)
    {
        var task = await LoadAsync(id, tracking: true, ct);
        if (task is null || !await CanSeeAsync(task, ct))
        {
            return HousekeepingErrors.NotFound;
        }

        if (version is { } v)
        {
            db.Entry(task).Property(t => t.Version).OriginalValue = v;
        }

        var result = change(task);
        if (result.IsFailure)
        {
            return result.Error;
        }

        if (after is not null && await after(task) is { IsFailure: true } failed)
        {
            db.ChangeTracker.Clear();
            return failed.Error;
        }

        if (action is not null)
        {
            audit.Record(new AuditEntry(action, "HousekeepingTask", id.ToString(),
                After: new { Status = task.Status.ToString(), task.AssignedTo, task.RejectReason }));
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return Conflict;
        }

        return (await ToDtosAsync([task], ct))[0];
    }

    /// <summary>Уборщик без прав управления видит только свои задачи и свободные — чужих не видит.</summary>
    private async Task<bool> CanSeeAsync(HousekeepingTask task, CancellationToken ct)
    {
        var permissions = await PermissionsAsync(ct);
        if (permissions.Contains(Permissions.HousekeepingManage) || permissions.Contains(Permissions.HousekeepingInspect))
        {
            return true;
        }

        return task.AssignedTo == currentUser.UserId || (task.AssignedTo is null && task.Status == HousekeepingTaskStatus.Pending);
    }

    private async Task<IReadOnlySet<string>> PermissionsAsync(CancellationToken ct) =>
        currentUser.UserId is { } userId && await access.GetAsync(userId, ct) is { } user ? user.Permissions : new HashSet<string>();

    private async Task<HousekeepingTask?> LoadAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var query = db.Set<HousekeepingTask>().Include(t => t.Items).Where(t => t.Id == id && t.HotelId == hotelId);
        return await (tracking ? query : query.AsNoTracking()).FirstOrDefaultAsync(ct);
    }

    private async Task<ChecklistTemplate> TemplateAsync(bool tracking, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var query = db.Set<ChecklistTemplate>().Where(t => t.HotelId == hotelId && t.IsDefault);
        var template = await (tracking ? query : query.AsNoTracking()).FirstOrDefaultAsync(ct);
        if (template is null)
        {
            template = new ChecklistTemplate(hotelId, "Стандартная уборка", ChecklistTemplateSeeder.DefaultItems);
            db.Set<ChecklistTemplate>().Add(template);
            await db.SaveChangesAsync(ct);
        }

        return template;
    }

    private string Format(DateTimeOffset? utc) =>
        utc is { } v ? clock.ToHotelTime(v).ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture) : null!;

    private async Task<List<HousekeepingTaskDto>> ToDtosAsync(IReadOnlyCollection<HousekeepingTask> tasks, CancellationToken ct)
    {
        var roomInfo = await rooms.GetManyAsync(tasks.Select(t => t.RoomId).Distinct(), ct);
        var names = await users.GetNamesAsync(tasks.SelectMany(t => new[] { t.AssignedTo, t.InspectedBy }).Where(i => i != null).Select(i => i!.Value), ct);
        var now = clock.UtcNow;
        var states = await bookings.TodayStatesAsync(now, clock.ToUtc(clock.Today.AddDays(1), TimeOnly.MinValue), ct);
        var permissions = await PermissionsAsync(ct);
        var me = currentUser.UserId;
        return tasks.Select(t =>
        {
            var room = roomInfo.GetValueOrDefault(t.RoomId);
            var actions = new List<string>();
            if (permissions.Contains(Permissions.HousekeepingManage))
            {
                if (t.Status is HousekeepingTaskStatus.Pending or HousekeepingTaskStatus.Assigned or HousekeepingTaskStatus.Rejected)
                {
                    actions.Add("assign");
                }

                if (t.Status is HousekeepingTaskStatus.Pending or HousekeepingTaskStatus.Assigned)
                {
                    actions.Add("delete");
                }

                if (t.IsOpen)
                {
                    actions.Add("edit");
                }
            }

            if (permissions.Contains(Permissions.HousekeepingInspect) && t.Status == HousekeepingTaskStatus.Completed)
            {
                actions.Add("inspect");
            }

            if (permissions.Contains(Permissions.HousekeepingWork))
            {
                if (t.Status == HousekeepingTaskStatus.Pending && t.AssignedTo is null)
                {
                    actions.Add("take");
                }

                if (t.AssignedTo == me && t.Status is HousekeepingTaskStatus.Assigned or HousekeepingTaskStatus.Rejected)
                {
                    actions.Add("start");
                }

                if (t.AssignedTo == me && t.Status == HousekeepingTaskStatus.InProgress)
                {
                    actions.Add("complete");
                }
            }

            var next = states.GetValueOrDefault(t.RoomId)?.NextStartsAt;
            return new HousekeepingTaskDto(
                t.Id, t.RoomId, room?.Number ?? "—", room?.RoomTypeName ?? "—", room?.Status.ToString() ?? "—",
                t.Status.ToString(), t.Priority.ToString(), t.Origin.ToString(),
                t.AssignedTo, t.AssignedTo is { } a ? names.GetValueOrDefault(a) : null,
                t.DueAt is null ? null : Format(t.DueAt), t.DueAt < now && t.IsOpen && t.Status is not HousekeepingTaskStatus.Completed,
                next is null ? null : Format(next), t.Notes, Format(t.CreatedAt),
                t.StartedAt is null ? null : Format(t.StartedAt), t.CompletedAt is null ? null : Format(t.CompletedAt),
                t.InspectedAt is null ? null : Format(t.InspectedAt), t.InspectedBy is { } i ? names.GetValueOrDefault(i) : null,
                t.RejectReason,
                t.Items.OrderBy(x => x.Position).Select(x => new ChecklistItemDto(x.Id, x.Position, x.Text, x.IsDone)).ToList(),
                actions, t.Version);
        }).ToList();
    }
}

internal sealed class MaintenanceService(
    DbContext db,
    IHotelContext hotel,
    IRoomDirectory rooms,
    IRoomStatusChanger roomStatus,
    IUserDirectory users,
    ICurrentUser currentUser,
    IAuditTrail audit,
    IHotelClock clock) : IMaintenanceService
{
    public async Task<IReadOnlyList<MaintenanceRequestDto>> ListAsync(MaintenanceQuery q, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var query = db.Set<MaintenanceRequest>().AsNoTracking().Where(m => m.HotelId == hotelId);
        query = Enum.TryParse<MaintenanceStatus>(q.Status, out var status)
            ? query.Where(m => m.Status == status)
            : query.Where(m => m.Status == MaintenanceStatus.New || m.Status == MaintenanceStatus.InProgress);
        return await ToDtosAsync(await query.OrderByDescending(m => m.Severity).ThenBy(m => m.CreatedAt).ToListAsync(ct), ct);
    }

    public async Task<IReadOnlyList<MaintenanceRequestDto>> MineAsync(CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var me = currentUser.UserId;
        var items = await db.Set<MaintenanceRequest>().AsNoTracking().Where(m => m.HotelId == hotelId && m.ReportedBy == me)
            .OrderByDescending(m => m.CreatedAt).Take(30).ToListAsync(ct);
        return await ToDtosAsync(items, ct);
    }

    public async Task<Result<MaintenanceRequestDto>> CreateAsync(CreateMaintenanceRequest r, CancellationToken ct)
    {
        var room = await rooms.GetAsync(r.RoomId, ct);
        if (room is null || room.IsArchived)
        {
            return Error.NotFound("room.not_found", "Номер не найден.");
        }

        var hotelId = await hotel.GetHotelIdAsync(ct);
        var request = MaintenanceRequest.Create(hotelId, room.Id, r.TaskId, Enum.Parse<MaintenanceType>(r.Type), Enum.Parse<MaintenanceSeverity>(r.Severity),
            r.Description, currentUser.UserId, clock.UtcNow);
        db.Set<MaintenanceRequest>().Add(request);
        audit.Record(new AuditEntry("maintenance.created", "MaintenanceRequest", request.Id.ToString(), After: new { room = room.Number, r.Type, r.Severity }));
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([request], ct))[0];
    }

    public async Task<Result<MaintenanceRequestDto>> UpdateAsync(Guid id, UpdateMaintenanceRequest r, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var request = await db.Set<MaintenanceRequest>().FirstOrDefaultAsync(m => m.Id == id && m.HotelId == hotelId, ct);
        if (request is null)
        {
            return HousekeepingErrors.RequestNotFound;
        }

        if (r.Version is { } version)
        {
            db.Entry(request).Property(m => m.Version).OriginalValue = version;
        }

        var result = request.ChangeStatus(Enum.Parse<MaintenanceStatus>(r.Status), r.Note, currentUser.UserId, clock.UtcNow);
        if (result.IsFailure)
        {
            return result.Error;
        }

        if (r.CloseRoom)
        {
            // Закрыть номер на ремонт: из «Свободен» или «Уборка»; занятый номер — сначала выселение.
            var closed = await roomStatus.ChangeAsync(request.RoomId, RoomStatus.Maintenance, RoomStatusSource.Manual, $"Заявка: {request.Description}", null, ct);
            if (closed.IsFailure)
            {
                db.ChangeTracker.Clear();
                return closed.Error;
            }
        }

        audit.Record(new AuditEntry("maintenance.updated", "MaintenanceRequest", id.ToString(), After: new { r.Status, r.Note, r.CloseRoom }));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return Error.Conflict("concurrency.conflict", "Заявку изменил другой пользователь. Обновите страницу.");
        }

        return (await ToDtosAsync([request], ct))[0];
    }

    private async Task<List<MaintenanceRequestDto>> ToDtosAsync(IReadOnlyCollection<MaintenanceRequest> items, CancellationToken ct)
    {
        var roomInfo = await rooms.GetManyAsync(items.Select(m => m.RoomId).Distinct(), ct);
        var names = await users.GetNamesAsync(items.SelectMany(m => new[] { m.ReportedBy, m.ResolvedBy }).Where(i => i != null).Select(i => i!.Value), ct);
        string F(DateTimeOffset v) => clock.ToHotelTime(v).ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);
        return items.Select(m =>
        {
            var room = roomInfo.GetValueOrDefault(m.RoomId);
            return new MaintenanceRequestDto(m.Id, m.RoomId, room?.Number ?? "—", m.TaskId, m.Type.ToString(), m.Severity.ToString(), m.Description,
                m.Status.ToString(), m.ReportedBy is { } r ? names.GetValueOrDefault(r) : null, F(m.CreatedAt),
                m.ResolvedAt is { } at ? F(at) : null, m.ResolvedBy is { } b ? names.GetValueOrDefault(b) : null, m.ResolutionNote,
                room?.Status.ToString() ?? "—", m.Version);
        }).ToList();
    }
}
