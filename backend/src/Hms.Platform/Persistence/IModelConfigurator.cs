using Microsoft.EntityFrameworkCore;

namespace Hms.Platform.Persistence;

/// <summary>
/// Вклад модуля в общую модель EF Core. Каждый модуль регистрирует свою реализацию в DI,
/// применяет в ней <c>IEntityTypeConfiguration</c> из своей сборки Infrastructure
/// и задаёт свою схему PostgreSQL.
/// </summary>
public interface IModelConfigurator
{
    void Configure(ModelBuilder modelBuilder);
}
