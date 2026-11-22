namespace Hms.Modules.Housekeeping.Contracts;

/// <summary>Задачи уборки для других модулей: после каждого выезда создаётся задача (ТЗ §3.3).</summary>
public interface IHousekeepingTasks
{
    /// <summary>
    /// Задача уборки после выезда гостя: срок — время уборки номера. Добавляется в ту же единицу работы,
    /// что и выезд (сохраняет вызывающий модуль). Возвращает id задачи.
    /// </summary>
    Task<Guid> CreateAfterCheckOutAsync(Guid roomId, Guid bookingId, DateTimeOffset dueAt, CancellationToken ct);
}

/// <summary>Задача уборки для отчётов (по сотрудникам, по номерам). Время — UTC.</summary>
public sealed record CleaningFact(Guid TaskId, Guid RoomId, string Status, string Origin, Guid? AssignedTo, Guid? InspectedBy,
    DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, DateTimeOffset? InspectedAt);

public interface IHousekeepingFacts
{
    /// <summary>Задачи, созданные или завершённые в [from, to) (удалённые не включаются).</summary>
    Task<IReadOnlyList<CleaningFact>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    /// <summary>Открытые задачи уборки сейчас (не принятые и не удалённые).</summary>
    Task<int> OpenCountAsync(CancellationToken ct);

    /// <summary>Уборки, которые закончены и ждут проверки администратором.</summary>
    Task<int> AwaitingInspectionCountAsync(CancellationToken ct);
}
