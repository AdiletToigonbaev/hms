using FluentValidation;
using Hms.Modules.Housekeeping.Domain;
using Hms.SharedKernel;

namespace Hms.Modules.Housekeeping.Application;

public sealed record ChecklistItemDto(Guid Id, int Position, string Text, bool IsDone);

/// <summary>
/// Задача уборки. Без персональных данных гостя: уборщик видит номер, срок, заметки и ближайший заезд в номер.
/// Время — по гостинице «yyyy-MM-ddTHH:mm».
/// </summary>
public sealed record HousekeepingTaskDto(
    Guid Id,
    Guid RoomId,
    string RoomNumber,
    string RoomTypeName,
    string RoomStatus,
    string Status,
    string Priority,
    string Origin,
    Guid? AssignedTo,
    string? AssignedToName,
    string? DueAt,
    bool IsOverdue,
    string? NextArrivalAt,
    string? Notes,
    string CreatedAt,
    string? StartedAt,
    string? CompletedAt,
    string? InspectedAt,
    string? InspectedByName,
    string? RejectReason,
    IReadOnlyList<ChecklistItemDto> Checklist,
    IReadOnlyList<string> AllowedActions,
    uint Version);

/// <param name="Status">Открытые (по умолчанию) или конкретный статус; «done» — проверенные.</param>
public sealed record TaskQuery(string? Status, Guid? RoomId, Guid? AssignedTo);

public sealed record CreateTaskRequest(Guid RoomId, string Priority, string? Notes, DateTimeOffset? DueAt);

public sealed record UpdateTaskRequest(string Priority, string? Notes, DateTimeOffset? DueAt, uint? Version);

public sealed record AssignTaskRequest(Guid UserId);

public sealed record ChecklistItemRequest(bool Done);

public sealed record InspectTaskRequest(bool Approved, string? Reason);

public sealed record StaffMemberDto(Guid Id, string Name, int OpenTasks);

public sealed record ChecklistTemplateDto(Guid Id, string Name, IReadOnlyList<string> Items, uint Version);

public sealed record SaveChecklistTemplateRequest(IReadOnlyList<string> Items, uint? Version);

public sealed record MaintenanceRequestDto(
    Guid Id,
    Guid RoomId,
    string RoomNumber,
    Guid? TaskId,
    string Type,
    string Severity,
    string Description,
    string Status,
    string? ReportedByName,
    string CreatedAt,
    string? ResolvedAt,
    string? ResolvedByName,
    string? ResolutionNote,
    string RoomStatus,
    uint Version);

public sealed record CreateMaintenanceRequest(Guid RoomId, Guid? TaskId, string Type, string Severity, string Description);

/// <param name="CloseRoom">Закрыть номер на ремонт (статус «Ремонт») — если номер свободен или на уборке.</param>
public sealed record UpdateMaintenanceRequest(string Status, string? Note, bool CloseRoom, uint? Version);

public sealed record MaintenanceQuery(string? Status);

public interface IHousekeepingService
{
    Task<IReadOnlyList<HousekeepingTaskDto>> ListAsync(TaskQuery query, CancellationToken ct);

    /// <summary>Задачи уборщика: свои незакрытые и свободные (не назначенные).</summary>
    Task<IReadOnlyList<HousekeepingTaskDto>> MyTasksAsync(CancellationToken ct);

    Task<IReadOnlyList<HousekeepingTaskDto>> MyHistoryAsync(CancellationToken ct);

    Task<Result<HousekeepingTaskDto>> GetAsync(Guid id, CancellationToken ct);

    Task<Result<HousekeepingTaskDto>> CreateAsync(CreateTaskRequest request, CancellationToken ct);

    Task<Result<HousekeepingTaskDto>> UpdateAsync(Guid id, UpdateTaskRequest request, CancellationToken ct);

    Task<Result> DeleteAsync(Guid id, CancellationToken ct);

    Task<Result<HousekeepingTaskDto>> AssignAsync(Guid id, AssignTaskRequest request, CancellationToken ct);

    Task<Result<HousekeepingTaskDto>> TakeAsync(Guid id, CancellationToken ct);

    Task<Result<HousekeepingTaskDto>> StartAsync(Guid id, CancellationToken ct);

    Task<Result<HousekeepingTaskDto>> SetItemAsync(Guid id, Guid itemId, ChecklistItemRequest request, CancellationToken ct);

    Task<Result<HousekeepingTaskDto>> CompleteAsync(Guid id, CancellationToken ct);

    Task<Result<HousekeepingTaskDto>> InspectAsync(Guid id, InspectTaskRequest request, CancellationToken ct);

    Task<IReadOnlyList<StaffMemberDto>> StaffAsync(CancellationToken ct);

    Task<ChecklistTemplateDto> GetTemplateAsync(CancellationToken ct);

    Task<Result<ChecklistTemplateDto>> SaveTemplateAsync(SaveChecklistTemplateRequest request, CancellationToken ct);
}

public interface IMaintenanceService
{
    Task<IReadOnlyList<MaintenanceRequestDto>> ListAsync(MaintenanceQuery query, CancellationToken ct);

    Task<IReadOnlyList<MaintenanceRequestDto>> MineAsync(CancellationToken ct);

    Task<Result<MaintenanceRequestDto>> CreateAsync(CreateMaintenanceRequest request, CancellationToken ct);

    Task<Result<MaintenanceRequestDto>> UpdateAsync(Guid id, UpdateMaintenanceRequest request, CancellationToken ct);
}

internal static class Enums
{
    public static bool Is<T>(string? value)
        where T : struct, Enum => Enum.TryParse<T>(value, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed);
}

public sealed class CreateTaskRequestValidator : AbstractValidator<CreateTaskRequest>
{
    public CreateTaskRequestValidator()
    {
        RuleFor(x => x.RoomId).NotEmpty().WithMessage("Выберите номер.");
        RuleFor(x => x.Priority).Must(Enums.Is<TaskPriority>).WithMessage("Выберите приоритет.");
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public sealed class UpdateTaskRequestValidator : AbstractValidator<UpdateTaskRequest>
{
    public UpdateTaskRequestValidator()
    {
        RuleFor(x => x.Priority).Must(Enums.Is<TaskPriority>).WithMessage("Выберите приоритет.");
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public sealed class InspectTaskRequestValidator : AbstractValidator<InspectTaskRequest>
{
    public InspectTaskRequestValidator() => RuleFor(x => x.Reason).MaximumLength(500);
}

public sealed class SaveChecklistTemplateRequestValidator : AbstractValidator<SaveChecklistTemplateRequest>
{
    public SaveChecklistTemplateRequestValidator()
    {
        RuleFor(x => x.Items).NotNull().Must(i => i.Count(t => !string.IsNullOrWhiteSpace(t)) is >= 1 and <= 40)
            .WithMessage("В чек-листе — от 1 до 40 пунктов.");
        RuleForEach(x => x.Items).MaximumLength(200).WithMessage("Пункт — не длиннее 200 символов.");
    }
}

public sealed class CreateMaintenanceRequestValidator : AbstractValidator<CreateMaintenanceRequest>
{
    public CreateMaintenanceRequestValidator()
    {
        RuleFor(x => x.RoomId).NotEmpty().WithMessage("Выберите номер.");
        RuleFor(x => x.Type).Must(Enums.Is<MaintenanceType>).WithMessage("Выберите тип.");
        RuleFor(x => x.Severity).Must(Enums.Is<MaintenanceSeverity>).WithMessage("Выберите срочность.");
        RuleFor(x => x.Description).NotEmpty().WithMessage("Опишите, что случилось.").MaximumLength(1000);
    }
}

public sealed class UpdateMaintenanceRequestValidator : AbstractValidator<UpdateMaintenanceRequest>
{
    public UpdateMaintenanceRequestValidator()
    {
        RuleFor(x => x.Status).Must(Enums.Is<MaintenanceStatus>).WithMessage("Выберите статус.");
        RuleFor(x => x.Note).MaximumLength(500);
    }
}
