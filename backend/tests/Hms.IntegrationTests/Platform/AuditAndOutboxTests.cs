using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hms.Modules.Identity.Application;
using Hms.Platform.Email;
using Hms.Platform.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static Hms.IntegrationTests.TestApi;

namespace Hms.IntegrationTests.Platform;

public class AuditAndOutboxTests(PostgresFixture db)
{
    private readonly TestApi _api = new(db);

    [Fact]
    public async Task Audit_RecordsLoginSuccessAndMaskedFailure_VisibleToOwner()
    {
        var (email, userId) = await _api.RegisterConfirmedGuestAsync();
        await _api.LoginAsync(email, GuestPassword);
        await _api.LoginRawAsync(email, "wrong-password-000");

        var owner = _api.Authorized((await _api.LoginOwnerAsync()).AccessToken);
        var page = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/audit-logs?entityType=User&entityId={userId}&pageSize=50", Ct);
        var actions = page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("action").GetString()).ToList();

        actions.Should().Contain(["auth.registered", "auth.email_confirmed", "auth.login.succeeded", "auth.login.failed"]);
        var registered = page.GetProperty("items").EnumerateArray().First(i => i.GetProperty("action").GetString() == "auth.registered");
        registered.GetProperty("after").GetProperty("consents").GetProperty("personalData").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Audit_UnknownAccountLogin_StoresMaskedLoginOnly()
    {
        var email = UniqueEmail("masked");
        await _api.LoginRawAsync(email, "wrong-password-000");

        var owner = _api.Authorized((await _api.LoginOwnerAsync()).AccessToken);
        var page = await owner.GetFromJsonAsync<JsonElement>("/api/v1/audit-logs?action=auth.login.failed&pageSize=100", Ct);
        var raw = page.GetRawText();

        raw.Should().NotContain(email, "полный логин не хранится в журнале");
        raw.Should().Contain(email[..2] + "***@test.local");
    }

    [Fact]
    public async Task Audit_IsAppendOnly_AtDatabaseLevel()
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync(Ct);

        foreach (var sql in new[] { "UPDATE audit.audit_logs SET action = 'x'", "DELETE FROM audit.audit_logs", "TRUNCATE audit.audit_logs" })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            var act = () => command.ExecuteNonQueryAsync(Ct);
            (await act.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Contain("append-only");
        }
    }

    [Fact]
    public async Task Outbox_SensitivePayload_IsClearedAfterDelivery()
    {
        var (email, _) = await _api.RegisterConfirmedGuestAsync();

        await using var scope = db.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DbContext>();
        var messages = await context.Set<OutboxMessage>().AsNoTracking()
            .Where(m => m.Type == "email.send" && m.Status == OutboxStatus.Done)
            .ToListAsync(Ct);

        messages.Should().NotBeEmpty();
        messages.Should().OnlyContain(m => m.Payload == "{}", "ссылки с токенами не хранятся после отправки");
        messages.Should().NotContain(m => m.Payload.Contains(email));
    }

    [Fact]
    public async Task Outbox_TransientFailure_RetriesWithBackoff_ThenDelivers()
    {
        var to = UniqueEmail("retry");
        var id = await EnqueueEmailAsync(to);
        db.Emails.FailWith = m => m.To == to ? new InvalidOperationException("SMTP недоступен") : null;
        try
        {
            await db.ProcessOutboxAsync();
            var failed = await FindAsync(id);
            failed.Status.Should().Be(OutboxStatus.Pending);
            failed.Attempts.Should().Be(1);
            failed.LastError.Should().Contain("SMTP");
            failed.NextAttemptAt.Should().BeAfter(DateTimeOffset.UtcNow.AddSeconds(5), "следующая попытка — после паузы");
        }
        finally
        {
            db.Emails.FailWith = null;
        }

        await MakeDueAsync(id);
        await db.ProcessOutboxAsync();
        (await FindAsync(id)).Status.Should().Be(OutboxStatus.Done);
        db.Emails.Sent.Should().ContainSingle(m => m.To == to);
    }

    [Fact]
    public async Task Outbox_PermanentFailure_StopsRetrying()
    {
        var to = UniqueEmail("permanent");
        var id = await EnqueueEmailAsync(to);
        db.Emails.FailWith = m => m.To == to ? new PermanentOutboxException("Адрес отклонён") : null;
        try
        {
            await db.ProcessOutboxAsync();
        }
        finally
        {
            db.Emails.FailWith = null;
        }

        var message = await FindAsync(id);
        message.Status.Should().Be(OutboxStatus.Failed);
        message.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task Outbox_DedupKey_PreventsDuplicateEnqueue()
    {
        var key = $"test:{Guid.NewGuid():N}";
        await EnqueueEmailAsync(UniqueEmail("dedup"), key);

        var duplicate = () => EnqueueEmailAsync(UniqueEmail("dedup"), key);

        await duplicate.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task RateLimit_TooManyLoginAttempts_Returns429()
    {
        await using var factory = db.CreateFactory(new Dictionary<string, string?> { ["RateLimiting:AuthStrictPerMinute"] = "3" });
        using var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            statuses.Add((await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(UniqueEmail(), "wrong-password-000"), Ct)).StatusCode);
        }

        statuses.Take(3).Should().OnlyContain(s => s == HttpStatusCode.Unauthorized);
        var limited = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(UniqueEmail(), "x-password-000"), Ct);
        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await ProblemCodeAsync(limited)).Should().Be("rate_limited");
        limited.Headers.RetryAfter.Should().NotBeNull();
    }

    private async Task<Guid> EnqueueEmailAsync(string to, string? dedupKey = null)
    {
        await using var scope = db.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DbContext>();
        scope.ServiceProvider.GetRequiredService<IEmailOutbox>().Enqueue(new EmailMessage(to, "Тест", "<p>Тест</p>", "Тест"), dedupKey);
        var id = context.ChangeTracker.Entries<OutboxMessage>().Single().Entity.Id;
        await context.SaveChangesAsync(Ct);
        return id;
    }

    private async Task<OutboxMessage> FindAsync(Guid id)
    {
        await using var scope = db.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DbContext>().Set<OutboxMessage>().AsNoTracking().SingleAsync(m => m.Id == id, Ct);
    }

    private async Task MakeDueAsync(Guid id)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("UPDATE platform.outbox_messages SET next_attempt_at = now() WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync(Ct);
    }
}
