using System.Net;
using Hms.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Hms.IntegrationTests;

public class DatabaseTests(PostgresFixture db)
{
    [Fact]
    public async Task Migrations_AreFullyApplied()
    {
        await using var scope = db.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<HmsDbContext>();

        var pending = await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken);

        pending.Should().BeEmpty();
    }

    [Fact]
    public async Task Migrations_HistoryTable_IsInPlatformSchema()
    {
        var exists = await ScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'platform' AND table_name = '__ef_migrations_history')");

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task BtreeGistExtension_IsInstalled()
    {
        var exists = await ScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'btree_gist')");

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExclusionConstraint_OnDaterange_RejectsOverlap_AllowsCheckoutDayCheckin()
    {
        // Проверка фундамента защиты от двойного бронирования (ТЗ §5): механизм, на котором
        // в Sprint 3 будет построено ограничение таблицы BookingRooms.
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await ExecAsync(connection, """
            CREATE TEMP TABLE stay_probe (
                room_id uuid NOT NULL,
                stay daterange NOT NULL,
                is_blocking boolean NOT NULL,
                EXCLUDE USING gist (room_id WITH =, stay WITH &&) WHERE (is_blocking)
            );
            """);

        var room = Guid.NewGuid();
        await ExecAsync(connection, $"INSERT INTO stay_probe VALUES ('{room}', '[2026-12-01,2026-12-05)', true)");

        // Выезд 5-го и заезд 5-го — не пересечение
        await ExecAsync(connection, $"INSERT INTO stay_probe VALUES ('{room}', '[2026-12-05,2026-12-07)', true)");
        // Отменённая (неблокирующая) бронь на те же даты допустима
        await ExecAsync(connection, $"INSERT INTO stay_probe VALUES ('{room}', '[2026-12-02,2026-12-03)', false)");
        // Другой номер на те же даты допустим
        await ExecAsync(connection, $"INSERT INTO stay_probe VALUES ('{Guid.NewGuid()}', '[2026-12-01,2026-12-05)', true)");

        var overlap = () => ExecAsync(connection, $"INSERT INTO stay_probe VALUES ('{room}', '[2026-12-04,2026-12-06)', true)");

        (await overlap.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.ExclusionViolation);
    }

    [Fact]
    public async Task HealthReady_WithDatabase_ReturnsHealthy()
    {
        using var client = db.Factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private static async Task ExecAsync(NpgsqlConnection connection, string sql)
    {
#pragma warning disable CA2100 // SQL в тесте — константы и сгенерированные Guid, пользовательского ввода нет
        await using var command = new NpgsqlCommand(sql, connection);
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
