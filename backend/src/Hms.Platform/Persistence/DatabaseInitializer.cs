using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hms.Platform.Persistence;

/// <summary>Применяет миграции и запускает сидеры. Используется командой <c>migrate</c> и тестами.</summary>
public static partial class DatabaseInitializer
{
    public static async Task MigrateAndSeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitializer));

        await provider.GetRequiredService<DbContext>().Database.MigrateAsync(cancellationToken);
        LogMigrated(logger);

        foreach (var seeder in provider.GetServices<IDataSeeder>().OrderBy(s => s.Order))
        {
            await seeder.SeedAsync(cancellationToken);
            LogSeeded(logger, seeder.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Миграции БД применены")]
    private static partial void LogMigrated(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Сидер {Seeder} выполнен")]
    private static partial void LogSeeded(ILogger logger, string seeder);
}
