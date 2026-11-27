using Hms.Modules.Bookings.Contracts;
using Hms.Modules.Hotel.Contracts;
using Hms.Modules.Messaging.Contracts;
using Hms.Modules.Messaging.Domain;
using Hms.Platform.Outbox;
using Hms.Platform.Time;
using Hms.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hms.Modules.Messaging.Infrastructure;

public sealed class MessagingJobsOptions
{
    public const string Section = "MessagingJobs";

    /// <summary>Фоновые напоминания (в интеграционных тестах выключены — тесты вызывают <see cref="IReminderScheduler.RunAsync"/> сами).</summary>
    public bool Enabled { get; set; } = true;

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);
}

public sealed record ReminderRun(int CheckIn, int CheckOut);

/// <summary>Один проход напоминаний: о заезде (подтверждённые брони) и о выезде (заселённые).</summary>
public interface IReminderScheduler
{
    Task<ReminderRun> RunAsync(DateTimeOffset now, CancellationToken ct);
}

/// <summary>
/// Ключ дедупликации включает время начала/конца: перенесённая или продлённая бронь получит новое напоминание,
/// а повторный проход планировщика — нет.
/// </summary>
internal sealed partial class ReminderScheduler(
    DbContext db,
    IHotelContext hotel,
    IBookingNotices bookings,
    NotificationDispatcher dispatcher,
    IHotelClock clock,
    ILogger<ReminderScheduler> logger) : IReminderScheduler
{
    // Посуточная бронь напоминается накануне — заглядываем на двое суток вперёд.
    private static readonly TimeSpan LookAhead = TimeSpan.FromHours(48);

    public async Task<ReminderRun> RunAsync(DateTimeOffset now, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var settings = await db.Set<MessagingSettings>().AsNoTracking().FirstOrDefaultAsync(s => s.HotelId == hotelId, ct);
        if (settings is null)
        {
            return new ReminderRun(0, 0);
        }

        var checkIn = 0;
        foreach (var b in await bookings.StartingAsync(now, now + LookAhead, ct))
        {
            var at = ReminderRules.StartReminderAt(settings, b.Kind == "Hourly", b.StartsAt, clock.TimeZone);
            if (b.GuestId is { } guestId && ReminderRules.IsDue(at, b.StartsAt, b.CreatedAt, now)
                && await dispatcher.DispatchAsync(new GuestNotification(NotificationEvent.CheckInReminder, guestId, b.BookingId), $"{TemplateCatalog.CheckInReminder}:{b.BookingId}:{b.StartsAt.UtcTicks}", ct) is not null)
            {
                checkIn++;
            }
        }

        var checkOut = 0;
        foreach (var b in await bookings.EndingAsync(now, now + LookAhead, ct))
        {
            var at = ReminderRules.EndReminderAt(settings, b.Kind == "Hourly", b.EndsAt);
            if (b.GuestId is { } guestId && ReminderRules.IsDue(at, b.EndsAt, null, now)
                && await dispatcher.DispatchAsync(new GuestNotification(NotificationEvent.CheckOutReminder, guestId, b.BookingId), $"{TemplateCatalog.CheckOutReminder}:{b.BookingId}:{b.EndsAt.UtcTicks}", ct) is not null)
            {
                checkOut++;
            }
        }

        await db.SaveChangesAsync(ct);
        if (checkIn + checkOut > 0)
        {
            LogRun(logger, checkIn, checkOut);
        }

        return new ReminderRun(checkIn, checkOut);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Напоминания: о заезде {CheckIn}, о выезде {CheckOut}")]
    private static partial void LogRun(ILogger logger, int checkIn, int checkOut);
}

internal sealed partial class ReminderBackgroundService(
    IServiceScopeFactory scopes,
    IOptions<MessagingJobsOptions> options,
    TimeProvider time,
    ILogger<ReminderBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.Value.Interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IReminderScheduler>().RunAsync(time.GetUtcNow(), stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogError(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Напоминания: ошибка прохода")]
    private static partial void LogError(ILogger logger, Exception exception);
}

/// <summary>Код подтверждения телефона в WhatsApp: шаблон категории «Аутентификация», только при одобренном шаблоне.</summary>
internal sealed class VerificationCodeSender(
    DbContext db,
    IHotelContext hotel,
    WhatsAppGateway gateway,
    ConversationRegistry conversations,
    IOutbox outbox,
    IHotelClock clock) : IVerificationCodeSender
{
    public async Task<bool> IsAvailableAsync(CancellationToken ct) => await TemplateAsync(ct) is not null && await gateway.IsAvailableAsync(ct);

    public async Task<Result> EnqueueAsync(string phoneE164, string code, Guid? guestId, CancellationToken ct)
    {
        var template = await TemplateAsync(ct);
        if (template is null || !await gateway.IsAvailableAsync(ct))
        {
            return MessagingErrors.WhatsAppUnavailable;
        }

        var now = clock.UtcNow;
        var message = Message.WhatsAppTemplateMessage(template.HotelId, guestId, null, MessageKind.VerificationCode, TemplateCatalog.VerificationCode, phoneE164,
            "•••••• — код подтверждения.", template.WhatsAppName!, template.Language, new Dictionary<string, string> { ["code"] = code }, ["code"],
            isAuthentication: true, emailFallback: false, dedupKey: null, sentBy: null, now);
        message.AttachConversation((await conversations.GetOrCreateAsync(template.HotelId, phoneE164, guestId, now, ct)).Id);
        db.Set<Message>().Add(message);
        outbox.Enqueue(MessagingOutbox.Deliver, new DeliverPayload(message.Id));
        return Result.Success();
    }

    private async Task<MessageTemplate?> TemplateAsync(CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var template = await db.Set<MessageTemplate>().AsNoTracking()
            .FirstOrDefaultAsync(t => t.HotelId == hotelId && t.Code == TemplateCatalog.VerificationCode && t.Language == TemplateCatalog.DefaultLanguage, ct);
        return template is { WhatsAppReady: true } ? template : null;
    }
}

/// <summary>Обезличивание переписки: адреса и тексты стираются, статусы и даты остаются.</summary>
internal sealed class GuestMessagesCleanup(DbContext db) : IGuestMessagesCleanup
{
    public async Task AnonymizeAsync(Guid guestId, CancellationToken ct)
    {
        await db.Set<Message>().Where(m => m.GuestId == guestId).ExecuteUpdateAsync(s => s
            .SetProperty(m => m.Recipient, (string?)null)
            .SetProperty(m => m.Subject, (string?)null)
            .SetProperty(m => m.Body, Message.AnonymizedText)
            .SetProperty(m => m.Parameters, (Dictionary<string, string>?)null)
            .SetProperty(m => m.ParameterNames, (string[]?)null), ct);
        // Диалоги гостя: сообщения этого номера без карточки тоже стираются — номер и тексты являются персональными данными.
        var conversationIds = await db.Set<Conversation>().Where(c => c.GuestId == guestId).Select(c => c.Id).ToListAsync(ct);
        await db.Set<Message>().Where(m => m.ConversationId != null && conversationIds.Contains(m.ConversationId.Value)).ExecuteUpdateAsync(s => s
            .SetProperty(m => m.Recipient, (string?)null)
            .SetProperty(m => m.Body, Message.AnonymizedText), ct);
        await db.Set<Conversation>().Where(c => conversationIds.Contains(c.Id)).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.GuestId, (Guid?)null)
            .SetProperty(c => c.ProfileName, (string?)null)
            .SetProperty(c => c.Phone, c => "deleted:" + c.Id.ToString()), ct);
    }
}
