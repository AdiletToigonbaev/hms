using Hms.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Hms.IntegrationTests;

/// <summary>
/// Поднимает PostgreSQL в Docker (та же мажорная версия, что в deploy/docker-compose.yml),
/// применяет миграции и создаёт хост API, подключённый к этой БД.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string PostgresImage = "postgres:18-alpine";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(PostgresImage)
        .WithDatabase("hms_test")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Hms", ConnectionString);
        });

        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<HmsDbContext>().Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }

        await _container.DisposeAsync();
    }
}
