using Hms.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.Extensions.DependencyInjection;

public static class PersistenceServiceCollectionExtensions
{
    public const string MigrationsHistorySchema = "platform";
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    /// <summary>Регистрирует <see cref="HmsDbContext"/> (PostgreSQL, snake_case-имена).</summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<HmsDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsAssembly(typeof(HmsDbContext).Assembly.GetName().Name)
                .MigrationsHistoryTable(MigrationsHistoryTable, MigrationsHistorySchema))
            .UseSnakeCaseNamingConvention());

        return services;
    }
}
