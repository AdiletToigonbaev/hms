using Hms.SharedKernel;

namespace Hms.Modules.Housekeeping.Domain;

/// <summary>
/// Статусы задачи уборки (ТЗ §3.3, docs/database/database-design.md §4):
/// Pending → Assigned → InProgress → Completed → Inspected | Rejected; после отказа уборщик убирает заново (Rejected → InProgress).
/// </summary>
public enum HousekeepingTaskStatus
{
    Pending,
    Assigned,
    InProgress,
    Completed,
    Inspected,
    Rejected,
}

public enum TaskPriority
{
    Normal,
    High,
}

/// <summary>Откуда задача: после выезда гостя (автоматически) или создана администратором.</summary>
public enum TaskOrigin
{
    CheckOut,
    Manual,
}

public static class HousekeepingErrors
{
    public static readonly Error NotFound = Error.NotFound("housekeeping.task_not_found", "Задача уборки не найдена.");
    public static readonly Error NotYours = Error.Forbidden("housekeeping.not_your_task", "Это задача другого сотрудника.");
    public static readonly Error ChecklistIncomplete = Error.BusinessRule("housekeeping.checklist_incomplete", "Отметьте все пункты чек-листа, прежде чем завершить уборку.");
    public static readonly Error ReasonRequired = Error.Validation("housekeeping.reason_required", "Укажите, что нужно исправить.");
    public static readonly Error ItemNotFound = Error.NotFound("housekeeping.item_not_found", "Пункт чек-листа не найден.");
    public static readonly Error RequestNotFound = Error.NotFound("maintenance.not_found", "Заявка не найдена.");

    public static Error InvalidTransition(HousekeepingTaskStatus from, string action) =>
        Error.Conflict("housekeeping.invalid_transition", $"Нельзя {action}: задача в статусе «{from}».");
}

/// <summary>
/// Задача уборки номера с чек-листом. Уборщик видит номер, срок и заметки — без персональных данных гостя.
/// Ошибочную задачу (ещё не начатую) администратор удаляет мягко, с записью в аудит.
/// </summary>
public sealed class HousekeepingTask
{
    private readonly List<ChecklistItem> _items = [];

    private HousekeepingTask()
    {
    }

    public Guid Id { get; private set; }

    public Guid HotelId { get; private set; }

    public Guid RoomId { get; private set; }

    /// <summary>Бронь, после выезда по которой создана задача.</summary>
    public Guid? BookingId { get; private set; }

    public TaskOrigin Origin { get; private set; }

    public HousekeepingTaskStatus Status { get; private set; }

    public TaskPriority Priority { get; private set; }

    public Guid? AssignedTo { get; private set; }

    /// <summary>К какому времени номер должен быть готов.</summary>
    public DateTimeOffset? DueAt { get; private set; }

    public string? Notes { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public Guid? InspectedBy { get; private set; }

    public DateTimeOffset? InspectedAt { get; private set; }

    public string? RejectReason { get; private set; }

    public bool IsDeleted { get; private set; }

    public uint Version { get; private set; }

    public IReadOnlyList<ChecklistItem> Items => _items;

    /// <summary>Задача ещё в работе (не закрыта проверкой и не удалена).</summary>
    public bool IsOpen => !IsDeleted && Status is not HousekeepingTaskStatus.Inspected;

    public static HousekeepingTask Create(
        Guid hotelId, Guid roomId, Guid? bookingId, TaskOrigin origin, TaskPriority priority, DateTimeOffset? dueAt, string? notes,
        IEnumerable<string> checklist, Guid? by, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(checklist);
        var task = new HousekeepingTask
        {
            Id = Guid.CreateVersion7(),
            HotelId = hotelId,
            RoomId = roomId,
            BookingId = bookingId,
            Origin = origin,
            Status = HousekeepingTaskStatus.Pending,
            Priority = priority,
            DueAt = dueAt,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            CreatedBy = by,
            CreatedAt = now,
        };
        var position = 0;
        foreach (var text in checklist.Where(t => !string.IsNullOrWhiteSpace(t)))
        {
            task._items.Add(new ChecklistItem(task.Id, ++position, text.Trim()));
        }

        return task;
    }

    /// <summary>Назначить уборщика (администратор). Начатую уборку не переназначить.</summary>
    public Result Assign(Guid userId)
    {
        if (IsDeleted || Status is not (HousekeepingTaskStatus.Pending or HousekeepingTaskStatus.Assigned or HousekeepingTaskStatus.Rejected))
        {
            return HousekeepingErrors.InvalidTransition(Status, "назначить");
        }

        AssignedTo = userId;
        if (Status == HousekeepingTaskStatus.Pending)
        {
            Status = HousekeepingTaskStatus.Assigned;
        }

        return Result.Success();
    }

    /// <summary>Уборщик берёт свободную задачу себе.</summary>
    public Result Take(Guid userId)
    {
        if (IsDeleted || Status != HousekeepingTaskStatus.Pending || AssignedTo is not null)
        {
            return HousekeepingErrors.InvalidTransition(Status, "взять задачу");
        }

        AssignedTo = userId;
        Status = HousekeepingTaskStatus.Assigned;
        return Result.Success();
    }

    /// <summary>Начать уборку (или начать заново после отказа на проверке).</summary>
    public Result Start(Guid userId, DateTimeOffset now)
    {
        if (AssignedTo != userId)
        {
            return HousekeepingErrors.NotYours;
        }

        if (IsDeleted || Status is not (HousekeepingTaskStatus.Assigned or HousekeepingTaskStatus.Rejected))
        {
            return HousekeepingErrors.InvalidTransition(Status, "начать уборку");
        }

        Status = HousekeepingTaskStatus.InProgress;
        StartedAt ??= now;
        return Result.Success();
    }

    public Result SetItem(Guid itemId, bool done, Guid userId, DateTimeOffset now)
    {
        if (AssignedTo != userId)
        {
            return HousekeepingErrors.NotYours;
        }

        if (Status != HousekeepingTaskStatus.InProgress)
        {
            return HousekeepingErrors.InvalidTransition(Status, "отметить пункт");
        }

        var item = _items.FirstOrDefault(i => i.Id == itemId);
        if (item is null)
        {
            return HousekeepingErrors.ItemNotFound;
        }

        item.Set(done, now);
        return Result.Success();
    }

    /// <summary>Уборка закончена: все пункты чек-листа отмечены.</summary>
    public Result Complete(Guid userId, DateTimeOffset now)
    {
        if (AssignedTo != userId)
        {
            return HousekeepingErrors.NotYours;
        }

        if (Status != HousekeepingTaskStatus.InProgress)
        {
            return HousekeepingErrors.InvalidTransition(Status, "завершить уборку");
        }

        if (_items.Any(i => !i.IsDone))
        {
            return HousekeepingErrors.ChecklistIncomplete;
        }

        Status = HousekeepingTaskStatus.Completed;
        CompletedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Проверка администратором: принято — номер готов; нет — с причиной, пункты чек-листа сбрасываются, уборщик убирает заново.
    /// </summary>
    public Result Inspect(bool approved, string? reason, Guid? by, DateTimeOffset now)
    {
        if (Status != HousekeepingTaskStatus.Completed)
        {
            return HousekeepingErrors.InvalidTransition(Status, "проверить");
        }

        if (!approved && string.IsNullOrWhiteSpace(reason))
        {
            return HousekeepingErrors.ReasonRequired;
        }

        InspectedBy = by;
        InspectedAt = now;
        if (approved)
        {
            Status = HousekeepingTaskStatus.Inspected;
            RejectReason = null;
        }
        else
        {
            Status = HousekeepingTaskStatus.Rejected;
            RejectReason = reason!.Trim();
            CompletedAt = null;
            foreach (var item in _items)
            {
                item.Set(false, now);
            }
        }

        return Result.Success();
    }

    /// <summary>Настройка владельца «освобождать номер сразу после уборки»: завершённая уборка принимается без проверки.</summary>
    public void AutoAccept(DateTimeOffset now)
    {
        if (Status == HousekeepingTaskStatus.Completed)
        {
            Status = HousekeepingTaskStatus.Inspected;
            InspectedAt = now;
            InspectedBy = null;
        }
    }

    public Result Update(TaskPriority priority, string? notes, DateTimeOffset? dueAt)
    {
        if (!IsOpen)
        {
            return HousekeepingErrors.InvalidTransition(Status, "изменить");
        }

        Priority = priority;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        DueAt = dueAt;
        return Result.Success();
    }

    /// <summary>Удалить ошибочную задачу — только пока уборка не начата.</summary>
    public Result Delete()
    {
        if (IsDeleted || Status is not (HousekeepingTaskStatus.Pending or HousekeepingTaskStatus.Assigned))
        {
            return HousekeepingErrors.InvalidTransition(Status, "удалить");
        }

        IsDeleted = true;
        return Result.Success();
    }
}

public sealed class ChecklistItem
{
    private ChecklistItem()
    {
        Text = string.Empty;
    }

    public ChecklistItem(Guid taskId, int position, string text)
    {
        Id = Guid.CreateVersion7();
        TaskId = taskId;
        Position = position;
        Text = text;
    }

    public Guid Id { get; private set; }

    public Guid TaskId { get; private set; }

    public int Position { get; private set; }

    public string Text { get; private set; }

    public bool IsDone { get; private set; }

    public DateTimeOffset? DoneAt { get; private set; }

    internal void Set(bool done, DateTimeOffset now)
    {
        IsDone = done;
        DoneAt = done ? now : null;
    }
}

/// <summary>Шаблон чек-листа: из него берутся пункты новой задачи. Пункты редактирует администратор.</summary>
public sealed class ChecklistTemplate
{
    private ChecklistTemplate()
    {
        Name = string.Empty;
        Items = [];
    }

    public ChecklistTemplate(Guid hotelId, string name, IEnumerable<string> items)
    {
        Id = Guid.CreateVersion7();
        HotelId = hotelId;
        Name = name;
        Items = items.ToList();
        IsDefault = true;
    }

    public Guid Id { get; private set; }

    public Guid HotelId { get; private set; }

    public string Name { get; private set; }

    public bool IsDefault { get; private set; }

    public List<string> Items { get; private set; }

    public uint Version { get; private set; }

    public void SetItems(IEnumerable<string> items) =>
        Items = items.Select(i => i.Trim()).Where(i => i.Length > 0).ToList();
}

public enum MaintenanceType
{
    Defect,
    Damage,
}

public enum MaintenanceSeverity
{
    Low,
    Normal,
    High,
    Critical,
}

public enum MaintenanceStatus
{
    New,
    InProgress,
    Resolved,
    Rejected,
}

/// <summary>Заявка о неисправности или повреждении в номере (уборщик или администратор).</summary>
public sealed class MaintenanceRequest
{
    private MaintenanceRequest()
    {
        Description = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid HotelId { get; private set; }

    public Guid RoomId { get; private set; }

    public Guid? TaskId { get; private set; }

    public MaintenanceType Type { get; private set; }

    public MaintenanceSeverity Severity { get; private set; }

    public string Description { get; private set; }

    public MaintenanceStatus Status { get; private set; }

    public Guid? ReportedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? ResolvedBy { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public string? ResolutionNote { get; private set; }

    public uint Version { get; private set; }

    public static MaintenanceRequest Create(Guid hotelId, Guid roomId, Guid? taskId, MaintenanceType type, MaintenanceSeverity severity, string description, Guid? by, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            HotelId = hotelId,
            RoomId = roomId,
            TaskId = taskId,
            Type = type,
            Severity = severity,
            Description = description.Trim(),
            Status = MaintenanceStatus.New,
            ReportedBy = by,
            CreatedAt = now,
        };

    public Result ChangeStatus(MaintenanceStatus status, string? note, Guid? by, DateTimeOffset now)
    {
        if (Status is MaintenanceStatus.Resolved or MaintenanceStatus.Rejected)
        {
            return Error.Conflict("maintenance.closed", "Заявка уже закрыта.");
        }

        if (status == MaintenanceStatus.Rejected && string.IsNullOrWhiteSpace(note))
        {
            return Error.Validation("maintenance.note_required", "Укажите, почему заявка отклонена.");
        }

        Status = status;
        ResolutionNote = string.IsNullOrWhiteSpace(note) ? ResolutionNote : note.Trim();
        if (status is MaintenanceStatus.Resolved or MaintenanceStatus.Rejected)
        {
            ResolvedBy = by;
            ResolvedAt = now;
        }

        return Result.Success();
    }
}
