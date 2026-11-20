using Microsoft.EntityFrameworkCore;

namespace Hms.Persistence;

/// <summary>
/// Единый контекст БД модульного монолита (см. docs/architecture/system-architecture.md §4).
/// Один контекст нужен, чтобы операции нескольких модулей (check-in/out, бронь + outbox)
/// выполнялись в одной транзакции. Изоляция модулей — через схемы и архитектурные тесты.
/// </summary>
public sealed class HmsDbContext : DbContext
{
    private readonly IReadOnlyList<IModelConfigurator> _configurators;

    public HmsDbContext(DbContextOptions<HmsDbContext> options, IEnumerable<IModelConfigurator> configurators)
        : base(options)
    {
        ArgumentNullException.ThrowIfNull(configurators);
        _configurators = configurators.ToList();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Нужен для EXCLUDE USING gist (RoomId WITH =, StayRange WITH &&) — защита от двойного бронирования.
        modelBuilder.HasPostgresExtension("btree_gist");

        foreach (var configurator in _configurators)
        {
            configurator.Configure(modelBuilder);
        }

        base.OnModelCreating(modelBuilder);
    }
}
