using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Hms.Modules.Identity.Application;
using Hms.Platform.Email;
using Hms.Platform.Outbox;
using Hms.Platform.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Hms.IntegrationTests.PostgresFixture))]

namespace Hms.IntegrationTests;

/// <summary>
/// Один PostgreSQL в Docker на все интеграционные тесты (та же мажорная версия, что в deploy/docker-compose.yml).
/// Применяет миграции и сидеры тем же кодом, что команда <c>migrate</c>, создаёт хост API и первого владельца.
/// Тесты изолируются уникальными данными (email/телефон), а не пересозданием БД.
/// </summary>
public sealed partial class PostgresFixture : IAsyncLifetime
{
    public const string PostgresImage = "postgres:18-alpine";
    public const string OwnerEmail = "owner@test.local";
    public const string OwnerInitialPassword = "owner-initial-password";
    public const string OwnerPassword = "owner-changed-password-1";
    // Ключ подписи генерируется на запуск тестов — в репозитории нет строк, похожих на секреты.
    /// <summary>Временный каталог для загруженных файлов (удаляется после тестов).</summary>
    public static readonly string FilesRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"hms-test-files-{Guid.NewGuid():N}");

    public static readonly string JwtKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(PostgresImage)
        .WithDatabase("hms_test")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public CapturingEmailSender Emails { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        Factory = CreateFactory();
        await DatabaseInitializer.MigrateAndSeedAsync(Factory.Services);

        // Первый вход владельца: временный пароль из Bootstrap → обязательная смена.
        using var client = Factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(OwnerEmail, OwnerInitialPassword));
        login.EnsureSuccessStatusCode();
        var tokens = (await login.Content.ReadFromJsonAsync<TokenResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var change = await client.PostAsJsonAsync("/api/v1/me/change-password", new ChangePasswordRequest(OwnerInitialPassword, OwnerPassword));
        change.EnsureSuccessStatusCode();
    }

    /// <summary>Хост API, подключённый к тестовой БД. Доп. настройки — для отдельных сценариев (например, лимиты).</summary>
    public WebApplicationFactory<Program> CreateFactory(IDictionary<string, string?>? overrides = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Hms", ConnectionString);
            builder.UseSetting("Jwt:SigningKey", JwtKey);
            builder.UseSetting("Outbox:Enabled", "false");
            builder.UseSetting("BookingJobs:Enabled", "false");
            builder.UseSetting("Identity:PublicWebUrl", "https://hotel.test");
            builder.UseSetting("Storage:RootPath", FilesRoot);
            builder.UseSetting("RateLimiting:AuthStrictPerMinute", "100000");
            builder.UseSetting("RateLimiting:AuthRefreshPerMinute", "100000");
            builder.UseSetting("RateLimiting:GuestBookingPerMinute", "100000");
            builder.UseSetting("GuestBookings:MaxActiveWebBookingsPerGuest", "1000");
            builder.UseSetting("Bootstrap:Owner:FullName", "Тестовый владелец");
            builder.UseSetting("Bootstrap:Owner:Email", OwnerEmail);
            builder.UseSetting("Bootstrap:Owner:Password", OwnerInitialPassword);
            foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(Emails);
            });
        });

    /// <summary>Обрабатывает outbox до пустой очереди (в тестах фоновая обработка выключена).</summary>
    public async Task ProcessOutboxAsync()
    {
        int processed;
        do
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            processed = await scope.ServiceProvider.GetRequiredService<IOutboxProcessor>().ProcessBatchAsync(CancellationToken.None);
        }
        while (processed > 0);
    }

    /// <summary>Достаёт userId и token из ссылки последнего письма адресату.</summary>
    public async Task<(Guid UserId, string Token)> TakeLinkAsync(string to, string path)
    {
        // Тесты идут параллельно: письмо может забрать обработчик outbox соседнего теста
        // (FOR UPDATE SKIP LOCKED) — ждём, пока оно будет отправлено тем или иным обработчиком.
        EmailMessage? message = null;
        for (var attempt = 0; attempt < 50 && message is null; attempt++)
        {
            await ProcessOutboxAsync();
            message = Emails.Sent.LastOrDefault(m => string.Equals(m.To, to, StringComparison.OrdinalIgnoreCase));
            if (message is null)
            {
                await Task.Delay(100);
            }
        }

        if (message is null)
        {
            throw new InvalidOperationException($"Письмо для {to} не отправлено.");
        }

        var match = LinkRegex().Match(message.TextBody);
        match.Success.Should().BeTrue($"в письме должна быть ссылка {path}");
        match.Groups["path"].Value.Should().Be(path);
        return (Guid.Parse(match.Groups["userId"].Value), Uri.UnescapeDataString(match.Groups["token"].Value));
    }

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }

        await _container.DisposeAsync();
        if (Directory.Exists(FilesRoot))
        {
            Directory.Delete(FilesRoot, recursive: true);
        }
    }

    [GeneratedRegex(@"https://hotel\.test(?<path>/[a-z-]+)\?userId=(?<userId>[0-9a-f-]{36})&token=(?<token>[^\s&]+)")]
    private static partial Regex LinkRegex();
}

/// <summary>Перехватчик писем вместо SMTP. Может имитировать сбой отправки.</summary>
public sealed class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public IReadOnlyList<EmailMessage> Sent => [.. _sent];

    /// <summary>Если задано — отправка падает с этой ошибкой (для тестов повторов outbox).</summary>
    public Func<EmailMessage, Exception?>? FailWith { get; set; }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (FailWith?.Invoke(message) is { } error)
        {
            throw error;
        }

        _sent.Enqueue(message);
        return Task.CompletedTask;
    }
}
