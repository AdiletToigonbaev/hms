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
