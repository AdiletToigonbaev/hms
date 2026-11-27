using FluentValidation;
using Hms.Modules.Hotel.Contracts;
using Hms.Modules.Identity.Contracts;
using Hms.Modules.Messaging.Application;
using Hms.Modules.Messaging.Contracts;
using Hms.Modules.Messaging.Domain;
using Hms.Platform.Http;
using Hms.Platform.Outbox;
using Hms.Platform.Persistence;
using Hms.Platform.Security;
using Hms.Platform.Time;
using Hms.Platform.Webhooks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hms.Modules.Messaging.Infrastructure;

public static class MessagingModule
{
    /// <summary>Тело вебхука Meta — до 3 МБ (документация).</summary>
    private const long MaxWebhookBytes = 3 * 1024 * 1024;

    public static IServiceCollection AddMessagingModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<WhatsAppOptions>(configuration.GetSection(WhatsAppOptions.Section));
        services.Configure<MessagingJobsOptions>(configuration.GetSection(MessagingJobsOptions.Section));
        services.AddSingleton<IModelConfigurator, MessagingModelConfigurator>();
        services.AddScoped<IDataSeeder, MessagingSeeder>();

        services.AddHttpClient<IWhatsAppClient, CloudApiWhatsAppClient>(c => c.Timeout = TimeSpan.FromSeconds(20));
        services.AddScoped<WhatsAppGateway>();
        services.AddScoped<IWhatsAppConnectionCheck, WhatsAppConnectionCheck>();

        services.AddScoped<MessageVariables>();
        services.AddScoped<ConversationRegistry>();
        services.AddScoped<NotificationDispatcher>();
        services.AddScoped<AutoReplyRenderer>();
        services.AddScoped<AutoResponder>();
        services.AddScoped<IGuestNotifications, GuestNotifications>();
        services.AddScoped<IGuestMessagesCleanup, GuestMessagesCleanup>();
        services.AddScoped<IVerificationCodeSender, VerificationCodeSender>();
        services.AddScoped<IOutboxHandler, NotifyOutboxHandler>();
        services.AddScoped<IOutboxHandler, DeliverOutboxHandler>();
        services.AddScoped<IOutboxHandler, WhatsAppWebhookHandler>();
        services.AddScoped<IReminderScheduler, ReminderScheduler>();
        services.AddHostedService<ReminderBackgroundService>();

        services.AddScoped<IMessageService, MessageService>();
        services.AddScoped<IConversationService, ConversationService>();
        services.AddScoped<ITemplateService, TemplateService>();
        services.AddScoped<IMessagingSettingsService, MessagingSettingsService>();
        services.AddValidatorsFromAssemblyContaining<SendMessageRequestValidator>(includeInternalTypes: true);
        return services;
    }

    public static IEndpointRouteBuilder MapMessagingEndpoints(this IEndpointRouteBuilder api)
    {
        // История и ручная отправка (email и WhatsApp) — сотрудники с whatsapp.send («Сообщения» в каталоге прав).
        var m = api.MapGroup("/messages").WithTags("Messaging");
        m.MapGet("/", async ([AsParameters] MessageQuery q, IMessageService s, CancellationToken ct) => Results.Ok(await s.ListAsync(q, ct)))
            .RequirePermission(Permissions.WhatsAppSend);
        m.MapGet("/send-options", async (Guid? guestId, Guid? conversationId, IMessageService s, CancellationToken ct) => (await s.SendOptionsAsync(guestId, conversationId, ct)).ToHttpResult())
            .RequirePermission(Permissions.WhatsAppSend);
        m.MapPost("/", async (SendMessageRequest r, IMessageService s, CancellationToken ct) =>
                (await s.SendAsync(r, ct)).ToHttpResult(x => Results.Created($"/api/v1/messages/{x.Id}", x)))
            .WithValidation<SendMessageRequest>().WithIdempotency("message.send").RequirePermission(Permissions.WhatsAppSend);
        m.MapPost("/{id:guid}/retry", async (Guid id, IMessageService s, CancellationToken ct) => (await s.RetryAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.WhatsAppSend);

        var c = api.MapGroup("/whatsapp/conversations").WithTags("Messaging");
        c.MapGet("/", async ([AsParameters] ConversationQuery q, IConversationService s, CancellationToken ct) => Results.Ok(await s.ListAsync(q, ct)))
            .RequirePermission(Permissions.WhatsAppSend);
        c.MapGet("/needs-operator-count", async (IConversationService s, CancellationToken ct) => Results.Ok(new { count = await s.NeedsOperatorCountAsync(ct) }))
            .RequirePermission(Permissions.WhatsAppSend);
        c.MapGet("/{id:guid}", async (Guid id, IConversationService s, CancellationToken ct) => (await s.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.WhatsAppSend);
        c.MapPost("/{id:guid}/read", async (Guid id, IConversationService s, CancellationToken ct) => (await s.MarkReadAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.WhatsAppSend);
        c.MapPost("/{id:guid}/resolve", async (Guid id, IConversationService s, CancellationToken ct) => (await s.ResolveAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.WhatsAppSend);

        // Шаблоны, напоминания и автоответчик — владелец (whatsapp.templates.manage). Список шаблонов видит и администратор.
        var t = api.MapGroup("/messaging").WithTags("Messaging");
        t.MapGet("/templates", async (ITemplateService s, CancellationToken ct) => Results.Ok(await s.ListAsync(ct)))
            .RequireAnyPermission(Permissions.WhatsAppTemplatesManage, Permissions.WhatsAppSend);
        t.MapPut("/templates/{code}/{language}", async (string code, string language, UpdateTemplateRequest r, ITemplateService s, CancellationToken ct) =>
                (await s.UpdateAsync(code, language, r, ct)).ToHttpResult())
            .WithValidation<UpdateTemplateRequest>().RequirePermission(Permissions.WhatsAppTemplatesManage);
        t.MapGet("/templates/{code}/{language}/preview", async (string code, string language, ITemplateService s, CancellationToken ct) =>
                (await s.PreviewAsync(code, language, ct)).ToHttpResult())
            .RequireAnyPermission(Permissions.WhatsAppTemplatesManage, Permissions.WhatsAppSend);
        t.MapGet("/settings", async (IMessagingSettingsService s, CancellationToken ct) => Results.Ok(await s.GetAsync(ct)))
            .RequirePermission(Permissions.WhatsAppTemplatesManage);
        t.MapPut("/settings", async (UpdateMessagingSettingsRequest r, IMessagingSettingsService s, CancellationToken ct) => (await s.UpdateAsync(r, ct)).ToHttpResult())
            .WithValidation<UpdateMessagingSettingsRequest>().RequirePermission(Permissions.WhatsAppTemplatesManage);
        t.MapGet("/auto-replies", async (IMessagingSettingsService s, CancellationToken ct) => Results.Ok(await s.GetAutoRepliesAsync(ct)))
            .RequirePermission(Permissions.WhatsAppTemplatesManage);
        t.MapPut("/auto-replies", async (SaveAutoRepliesRequest r, IMessagingSettingsService s, CancellationToken ct) => (await s.SaveAutoRepliesAsync(r, ct)).ToHttpResult())
            .WithValidation<SaveAutoRepliesRequest>().RequirePermission(Permissions.WhatsAppTemplatesManage);
        t.MapPost("/auto-replies/test", async (AutoReplyTestRequest r, IMessagingSettingsService s, CancellationToken ct) => Results.Ok(await s.TestAutoReplyAsync(r, ct)))
            .WithValidation<AutoReplyTestRequest>().RequirePermission(Permissions.WhatsAppTemplatesManage);

        // Вебхук Meta: публичный адрес, подлинность — подпись X-Hub-Signature-256 (App Secret).
        var w = api.MapGroup("/webhooks/whatsapp").WithTags("Webhooks").AllowAnonymous();
        w.MapGet("/", VerifyWebhook);
        w.MapPost("/", ReceiveWebhookAsync);
        return api;
    }

    /// <summary>Подтверждение адреса (Meta → App Dashboard): hub.mode=subscribe, hub.verify_token совпадает — отвечаем hub.challenge.</summary>
    private static IResult VerifyWebhook(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? token,
        [FromQuery(Name = "hub.challenge")] string? challenge,
        IOptions<WhatsAppOptions> options)
    {
        var expected = options.Value.VerifyToken;
        if (mode != "subscribe" || string.IsNullOrEmpty(expected) || token is null || challenge is null
            || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(token), System.Text.Encoding.UTF8.GetBytes(expected)))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        return Results.Text(challenge, "text/plain");
    }

    /// <summary>Приём события: проверка подписи по сырому телу, сохранение (повтор отсекается), обработка — фоном через outbox.</summary>
    private static async Task<IResult> ReceiveWebhookAsync(HttpRequest request, IWebhookInbox inbox, IOptions<WhatsAppOptions> options, ILoggerFactory loggers, CancellationToken ct)
    {
        if (!options.Value.SecretsConfigured)
        {
            return Results.NotFound();
        }

        if (request.ContentLength > MaxWebhookBytes)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, ct);
        if (buffer.Length > MaxWebhookBytes)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var body = buffer.ToArray();
        if (!WhatsAppSignature.IsValid(body, request.Headers[WhatsAppSignature.Header].ToString(), options.Value.AppSecret))
        {
            WebhookLog.InvalidSignature(loggers.CreateLogger("Hms.Messaging.Webhook"));
            return Results.Unauthorized();
        }

        string payload;
        try
        {
            payload = System.Text.Encoding.UTF8.GetString(body);
            using var _ = System.Text.Json.JsonDocument.Parse(payload);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or ArgumentException)
        {
            return Results.BadRequest();
        }

        await inbox.AcceptAsync("whatsapp", payload, ct);
        return Results.Ok();
    }
}

internal static partial class WebhookLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "WhatsApp webhook: неверная подпись — запрос отклонён")]
    public static partial void InvalidSignature(ILogger logger);
}

/// <summary>Схема <c>messaging</c>: сообщения, диалоги WhatsApp, шаблоны, настройки, меню автоответчика.</summary>
internal sealed class MessagingModelConfigurator : IModelConfigurator
{
    public const string Schema = "messaging";

    public void Configure(ModelBuilder b)
    {
        var dictionaryComparer = new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<Dictionary<string, string>?>(
            (x, y) => x == null ? y == null : y != null && x.Count == y.Count && !x.Except(y).Any(),
            v => v == null ? 0 : v.Aggregate(0, (h, kv) => HashCode.Combine(h, kv.Key, kv.Value)),
            v => v == null ? null : new Dictionary<string, string>(v));

        b.Entity<Message>(e =>
        {
            e.ToTable("messages", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Channel).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Direction).HasConversion<string>().HasMaxLength(5);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.TemplateCode).HasMaxLength(50);
            e.Property(x => x.Recipient).HasMaxLength(256);
            e.Property(x => x.Subject).HasMaxLength(200);
            // Текст без ограничения длины: письма и ответы автоответчика бывают длинными; WhatsApp обрезается при отправке (лимит Meta 4096).
            e.Property(x => x.Body).IsRequired();
            e.Property(x => x.WhatsAppTemplate).HasMaxLength(512);
            e.Property(x => x.WhatsAppLanguage).HasMaxLength(10);
            // Явный JSON-конвертер (как у настроек интеграций): без него Npgsql требует «динамической» сериализации.
            e.Property(x => x.Parameters)
                .HasColumnType("jsonb")
                .HasConversion(
                    v => v == null ? null : System.Text.Json.JsonSerializer.Serialize(v, System.Text.Json.JsonSerializerOptions.Web),
                    v => v == null ? null : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(v, System.Text.Json.JsonSerializerOptions.Web),
                    dictionaryComparer);
            e.Property(x => x.Error).HasMaxLength(500);
            e.Property(x => x.ExternalId).HasMaxLength(200);
            e.Property(x => x.DedupKey).HasMaxLength(200);
            e.Property(x => x.Version).IsRowVersion();
            e.HasIndex(x => new { x.HotelId, x.ExternalId }).IsUnique().HasFilter("external_id IS NOT NULL");
            e.HasIndex(x => new { x.HotelId, x.DedupKey }).IsUnique().HasFilter("dedup_key IS NOT NULL");
            e.HasIndex(x => new { x.GuestId, x.CreatedAt });
            e.HasIndex(x => new { x.ConversationId, x.CreatedAt });
            e.HasIndex(x => x.BookingId);
            e.HasIndex(x => new { x.HotelId, x.CreatedAt });
        });

        b.Entity<Conversation>(e =>
        {
            e.ToTable("conversations", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Phone).HasMaxLength(64).IsRequired();
            e.Property(x => x.ProfileName).HasMaxLength(200);
            e.Ignore(x => x.WindowClosesAt);
            e.HasIndex(x => new { x.HotelId, x.Phone }).IsUnique();
            e.HasIndex(x => x.GuestId);
            e.HasIndex(x => new { x.HotelId, x.LastMessageAt });
        });

        b.Entity<MessageTemplate>(e =>
        {
            e.ToTable("templates", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Code).HasMaxLength(50).IsRequired();
            e.Property(x => x.Language).HasMaxLength(10).IsRequired();
            e.Property(x => x.EmailSubject).HasMaxLength(150).IsRequired();
            e.Property(x => x.EmailBody).HasMaxLength(4000).IsRequired();
            e.Property(x => x.WhatsAppName).HasMaxLength(512);
            e.Property(x => x.WhatsAppBody).HasMaxLength(1024).IsRequired();
            e.Property(x => x.WhatsAppStatus).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Version).IsRowVersion();
            e.Ignore(x => x.WhatsAppReady);
            e.HasIndex(x => new { x.HotelId, x.Code, x.Language }).IsUnique();
        });

        b.Entity<MessagingSettings>(e =>
        {
            e.ToTable("settings", Schema);
            e.HasKey(x => x.HotelId);
            e.Property(x => x.HotelId).ValueGeneratedNever();
            e.Property(x => x.GreetingText).HasMaxLength(1000).IsRequired();
            e.Property(x => x.FallbackText).HasMaxLength(1000).IsRequired();
            e.Property(x => x.StopText).HasMaxLength(1000).IsRequired();
            e.Property(x => x.Version).IsRowVersion();
        });

        b.Entity<AutoReply>(e =>
        {
            e.ToTable("auto_replies", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Title).HasMaxLength(40).IsRequired();
            e.Property(x => x.Body).HasMaxLength(1500).IsRequired();
            e.Property(x => x.Action).HasConversion<string>().HasMaxLength(10);
            e.HasIndex(x => new { x.HotelId, x.Number }).IsUnique();
        });
    }
}

/// <summary>
/// Шаблоны из каталога (недостающие коды — при каждом релизе), настройки и меню автоответчика по умолчанию — один раз.
/// Тексты, изменённые владельцем, не перезаписываются.
/// </summary>
internal sealed class MessagingSeeder(DbContext db, IHotelContext hotel, IHotelClock clock) : IDataSeeder
{
    public int Order => 45;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var hotelId = await hotel.GetHotelIdAsync(cancellationToken);
        var now = clock.UtcNow;
        var existing = await db.Set<MessageTemplate>().Where(t => t.HotelId == hotelId && t.Language == TemplateCatalog.DefaultLanguage)
            .Select(t => t.Code).ToListAsync(cancellationToken);
        foreach (var definition in TemplateCatalog.All.Where(d => !existing.Contains(d.Code)))
        {
            db.Set<MessageTemplate>().Add(MessageTemplate.FromDefinition(hotelId, definition, now));
        }

        if (!await db.Set<MessagingSettings>().AnyAsync(s => s.HotelId == hotelId, cancellationToken))
        {
            db.Set<MessagingSettings>().Add(MessagingSettings.CreateDefault(hotelId, now));
            if (!await db.Set<AutoReply>().AnyAsync(a => a.HotelId == hotelId, cancellationToken))
            {
                db.Set<AutoReply>().AddRange(AutoReply.Defaults(hotelId));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
