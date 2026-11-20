using Hms.Platform.Persistence;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Hms.Persistence;

/// <summary>
/// Единый контекст БД модульного монолита (см. docs/architecture/system-architecture.md §4).
/// Один контекст нужен, чтобы операции нескольких модулей (check-in/out, бронь + outbox)
/// выполнялись в одной транзакции. Изоляция модулей — через схемы и архитектурные тесты.
/// </summary>
public sealed class HmsDbContext : DbContext, IDataProtectionKeyContext
{
    private readonly IReadOnlyList<IModelConfigurator> _configurators;

    public HmsDbContext(DbContextOptions<HmsDbContext> options, IEnumerable<IModelConfigurator> configurators)
        : base(options)
    {
        ArgumentNullException.ThrowIfNull(configurators);
        _configurators = configurators.ToList();
    }

    /// <summary>
    /// Ключи ASP.NET Core Data Protection (токены подтверждения email и сброса пароля).
    /// Хранятся в БД, чтобы ссылки из писем работали после перезапуска и на любом экземпляре API.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Нужен для EXCLUDE USING gist (RoomId WITH =, StayRange WITH &&) — защита от двойного бронирования.
        modelBuilder.HasPostgresExtension("btree_gist");

        modelBuilder.Entity<DataProtectionKey>().ToTable("data_protection_keys", "platform");

        foreach (var configurator in _configurators)
        {
            configurator.Configure(modelBuilder);
        }

        base.OnModelCreating(modelBuilder);
    }
}
