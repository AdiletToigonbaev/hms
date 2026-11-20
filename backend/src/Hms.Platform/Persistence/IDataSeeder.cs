namespace Hms.Platform.Persistence;

/// <summary>
/// Идемпотентное заполнение справочников и начальных данных (роли, права, первый владелец).
/// Выполняется командой <c>migrate</c> после применения миграций — при каждом релизе.
/// </summary>
public interface IDataSeeder
{
    /// <summary>Порядок выполнения (меньше — раньше).</summary>
    int Order { get; }

    Task SeedAsync(CancellationToken cancellationToken);
}
