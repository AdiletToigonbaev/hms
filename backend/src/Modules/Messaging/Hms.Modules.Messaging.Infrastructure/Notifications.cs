using System.Globalization;
using Hms.Modules.Bookings.Contracts;
using Hms.Modules.Guests.Contracts;
using Hms.Modules.Hotel.Contracts;
using Hms.Modules.Messaging.Contracts;
using Hms.Modules.Messaging.Domain;
using Hms.Modules.Rooms.Contracts;
using Hms.Platform.Email;
using Hms.Platform.Outbox;
using Hms.Platform.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Hms.Modules.Messaging.Infrastructure;

/// <summary>Типы outbox-сообщений модуля. В payload только идентификаторы — без текста и контактов гостя.</summary>
internal static class MessagingOutbox
{
    public const string Notify = "messaging.notify";
    public const string Deliver = "messaging.deliver";
    public const string WhatsAppWebhook = "webhook.whatsapp";
}

internal sealed record DeliverPayload(Guid MessageId);

/// <summary>Уведомление ставится в outbox в единице работы вызывающего модуля.</summary>
internal sealed class GuestNotifications(IOutbox outbox) : IGuestNotifications
{
    public void Enqueue(GuestNotification notification) => outbox.Enqueue(MessagingOutbox.Notify, notification);
}

/// <summary>Переменные для текстов: гостиница, гость, бронь. Время — по часам гостиницы.</summary>
internal sealed class MessageVariables(IHotelProfile hotel, IHotelClock clock, IRoomDirectory rooms, IConfiguration configuration)
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    private HotelProfile? _profile;

    /// <summary>Адрес сайта — тот же, что в письмах Identity (<c>PUBLIC_WEB_URL</c>).</summary>
    public string SiteUrl => (configuration["Identity:PublicWebUrl"] ?? "http://localhost:3000").TrimEnd('/');

    public async Task<HotelProfile> ProfileAsync(CancellationToken ct) => _profile ??= await hotel.GetAsync(ct);

    public async Task<Dictionary<string, string>> HotelAsync(CancellationToken ct)
    {
        var p = await ProfileAsync(ct);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["hotel_name"] = p.Name,
            ["hotel_contacts"] = Contacts(p),
            ["site_url"] = SiteUrl,
        };
    }

    public async Task<Dictionary<string, string>> BookingAsync(BookingNotice b, string guestName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(b);
        var values = await HotelAsync(ct);
        var room = await rooms.GetAsync(b.RoomId, ct);
        var hourly = b.Kind == "Hourly";
        var start = clock.ToHotelTime(b.StartsAt);
        var end = clock.ToHotelTime(b.EndsAt);
        values["guest_name"] = guestName;
        values["booking_number"] = b.Number;
        values["starts_at"] = DateTime(start);
        values["ends_at"] = DateTime(end);
        values["stay_info"] = hourly
            ? start.Date == end.Date
                ? $"Время: {start.ToString("dd.MM.yyyy", Ru)} с {start:HH:mm} до {end:HH:mm}."
                : $"Время: с {DateTime(start)} до {DateTime(end)}."
            : $"Заезд: {start.ToString("dd.MM.yyyy", Ru)} с {start:HH:mm}, выезд: {end.ToString("dd.MM.yyyy", Ru)} до {end:HH:mm}.";
        values["room_number"] = room?.Number ?? "—";
        values["room_type"] = room?.RoomTypeName ?? "—";
        values["total_amount"] = TemplateText.Money(b.TotalAmount);
        values["booking_url"] = $"{SiteUrl}/account/bookings/{b.BookingId}";
        values["cancel_info"] = b.Status switch
        {
            BookingStatus.Expired => "Бронь не была подтверждена вовремя. Забронировать снова можно на сайте.",
            _ when b.PenaltyAmount > 0 => $"Штраф по правилам отмены: {TemplateText.Money(b.PenaltyAmount)}.",
            _ => string.Empty,
        };
        return values;
    }

    public static string Contacts(HotelProfile p) =>
        string.Join("\n", new[] { p.Phone is null ? null : $"Тел.: {p.Phone}", p.Address, p.Email }.Where(x => !string.IsNullOrWhiteSpace(x)));

    private static string DateTime(DateTimeOffset local) => local.ToString("dd.MM.yyyy HH:mm", Ru);
}

/// <summary>
/// Создание сообщения по событию. Канал (решение заказчика 2026-11-27): WhatsApp, если гость дал согласие,
/// WhatsApp подключён и шаблон одобрен в Meta; иначе — email. Если WhatsApp окончательно не доставил — письмо-замена.
/// Нет ни одного канала — запись «не отправлено» в истории (кроме шаблонов «только WhatsApp»).
/// </summary>
internal sealed partial class NotificationDispatcher(
    DbContext db,
    IHotelContext hotel,
    IGuestMessaging guests,
    IBookingNotices bookings,
    MessageVariables variables,
    WhatsAppGateway whatsApp,
    ConversationRegistry conversations,
    IOutbox outbox,
    IHotelClock clock,
    ILogger<NotificationDispatcher> logger)
{
    public static string DedupKey(GuestNotification n) => n.Event switch
    {
        NotificationEvent.RegistrationConfirmed => $"{TemplateCatalog.RegistrationConfirmed}:{n.GuestId}",
        NotificationEvent.PaymentReceived or NotificationEvent.RefundPaid => $"{TemplateCatalog.For(n.Event).Code}:{n.EntityId}",
        _ => $"{TemplateCatalog.For(n.Event).Code}:{n.BookingId}",
    };

    /// <returns>Созданное сообщение или null (событие выключено, повтор, гость удалён, нет канала для «только WhatsApp»).</returns>
    public async Task<Message?> DispatchAsync(GuestNotification n, string? dedupKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(n);
        var definition = TemplateCatalog.For(n.Event);
        var key = dedupKey ?? DedupKey(n);
        var hotelId = await hotel.GetHotelIdAsync(ct);
        if (await db.Set<Message>().AnyAsync(m => m.HotelId == hotelId && m.DedupKey == key, ct))
        {
            return null;
        }

        var template = await db.Set<MessageTemplate>().AsNoTracking()
            .FirstOrDefaultAsync(t => t.HotelId == hotelId && t.Code == definition.Code && t.Language == TemplateCatalog.DefaultLanguage, ct);
        if (template is not { IsActive: true } || n.GuestId is not { } guestId)
        {
            return null;
        }

        var guest = await guests.GetAsync(guestId, ct);
        if (guest is null || guest.IsAnonymized)
        {
            return null;
        }

        Dictionary<string, string> values;
        if (n.BookingId is { } bookingId)
        {
            var booking = await bookings.GetAsync(bookingId, ct);
            if (booking is null)
            {
                return null;
            }

            values = await variables.BookingAsync(booking, guest.FullName, ct);
        }
        else
        {
            values = await variables.HotelAsync(ct);
            values["guest_name"] = guest.FullName;
        }

        if (n.Amount is { } amount)
        {
            values["amount"] = TemplateText.Money(amount);
        }

        var now = clock.UtcNow;
        var phoneNumberId = await whatsApp.PhoneNumberIdAsync(ct);
        Message message;
        if (phoneNumberId is not null && guest.WhatsAppConsent && guest.WhatsAppNumber is { } phone && template.WhatsAppReady)
        {
            var names = TemplateText.Placeholders(template.WhatsAppBody).ToArray();
            message = Message.WhatsAppTemplateMessage(hotelId, guest.Id, n.BookingId, MessageKind.Notification, definition.Code, phone,
                TemplateText.Render(template.WhatsAppBody, values), template.WhatsAppName!, template.Language, values, names, definition.IsAuthentication,
                emailFallback: definition.EmailAllowed && guest.Email is not null, key, sentBy: null, now);
            message.AttachConversation((await conversations.GetOrCreateAsync(hotelId, phone, guest.Id, now, ct)).Id);
        }
        else if (definition.EmailAllowed && guest.Email is { } email)
        {
            message = Message.Email(hotelId, guest.Id, n.BookingId, MessageKind.Notification, definition.Code, email,
                TemplateText.Render(template.EmailSubject, values), TemplateText.Render(template.EmailBody, values), key, sentBy: null, now);
        }
        else if (!definition.EmailAllowed)
        {
            return null;
        }
        else
        {
            var reason = guest.WhatsAppConsent ? "Нет email, а WhatsApp не подключён или шаблон не одобрен в Meta." : "Нет email и согласия на WhatsApp.";
            db.Set<Message>().Add(Message.Skipped(hotelId, guest.Id, n.BookingId, definition.Code, reason, key, now));
            LogSkipped(logger, definition.Code);
            return null;
        }

        db.Set<Message>().Add(message);
        outbox.Enqueue(MessagingOutbox.Deliver, new DeliverPayload(message.Id));
        return message;
    }

    /// <summary>Письмо вместо окончательно недоставленного WhatsApp-уведомления (переменные сохранены в сообщении).</summary>
    public async Task<Message?> EmailFallbackAsync(Message failed, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(failed);
        if (!failed.EmailFallback || failed.TemplateCode is null || failed.GuestId is not { } guestId || failed.Parameters is null)
        {
            return null;
        }

        var guest = await guests.GetAsync(guestId, ct);
        if (guest?.Email is not { } email || guest.IsAnonymized)
        {
            return null;
        }

        var template = await db.Set<MessageTemplate>().AsNoTracking()
            .FirstOrDefaultAsync(t => t.HotelId == failed.HotelId && t.Code == failed.TemplateCode && t.Language == TemplateCatalog.DefaultLanguage, ct);
        if (template is null)
        {
            return null;
        }

        var message = Message.Email(failed.HotelId, guestId, failed.BookingId, failed.Kind, failed.TemplateCode, email,
            TemplateText.Render(template.EmailSubject, failed.Parameters), TemplateText.Render(template.EmailBody, failed.Parameters),
            dedupKey: null, failed.SentBy, clock.UtcNow, fallbackFor: failed.Id);
        db.Set<Message>().Add(message);
        outbox.Enqueue(MessagingOutbox.Deliver, new DeliverPayload(message.Id));
        return message;
    }

    // Код шаблона без данных гостя.
    [LoggerMessage(Level = LogLevel.Information, Message = "Уведомление {Template} не отправлено: нет канала связи с гостем")]
    private static partial void LogSkipped(ILogger logger, string template);
}

/// <summary>Диалоги WhatsApp по номеру: один на номер в гостинице.</summary>
internal sealed class ConversationRegistry(DbContext db)
{
    public async Task<Conversation> GetOrCreateAsync(Guid hotelId, string phone, Guid? guestId, DateTimeOffset now, CancellationToken ct)
    {
        var existing = db.Set<Conversation>().Local.FirstOrDefault(c => c.HotelId == hotelId && c.Phone == phone)
            ?? await db.Set<Conversation>().FirstOrDefaultAsync(c => c.HotelId == hotelId && c.Phone == phone, ct);
        if (existing is not null)
        {
            if (guestId is { } g && existing.GuestId is null)
            {
                existing.LinkGuest(g);
            }

            return existing;
        }

        var created = Conversation.Start(hotelId, phone, guestId, now);
        db.Set<Conversation>().Add(created);
        return created;
    }
}

/// <summary>Outbox: событие другого модуля → сообщение гостю.</summary>
internal sealed class NotifyOutboxHandler(NotificationDispatcher dispatcher) : OutboxHandler<GuestNotification>
{
    public override string Type => MessagingOutbox.Notify;

    protected override Task HandleAsync(GuestNotification payload, CancellationToken cancellationToken) =>
        dispatcher.DispatchAsync(payload, dedupKey: null, cancellationToken);
}

/// <summary>Временная ошибка доставки: outbox повторит сообщение с паузой.</summary>
internal sealed class TemporaryDeliveryException(string message) : Exception(message);

/// <summary>
/// Outbox: отправка одного сообщения. Временные ошибки — повтор (outbox, пауза растёт), после <see cref="Message.MaxAttempts"/> —
/// «не отправлено». Окончательная ошибка WhatsApp — письмо-замена, если разрешено.
/// </summary>
internal sealed partial class DeliverOutboxHandler(
    DbContext db,
    IWhatsAppClient client,
    WhatsAppGateway gateway,
    IEmailSender email,
    IEmailComposer composer,
    NotificationDispatcher dispatcher,
    IHotelClock clock,
    ILogger<DeliverOutboxHandler> logger) : OutboxHandler<DeliverPayload>
{
    public override string Type => MessagingOutbox.Deliver;

    protected override async Task HandleAsync(DeliverPayload payload, CancellationToken cancellationToken)
    {
        var message = await db.Set<Message>().FirstOrDefaultAsync(m => m.Id == payload.MessageId, cancellationToken);
        if (message is null || message.Status != MessageStatus.Queued || message.Recipient is null)
        {
            return;
        }

        if (message.Channel == MessageChannel.Email)
        {
            await SendEmailAsync(message, cancellationToken);
            return;
        }

        var phoneNumberId = await gateway.PhoneNumberIdAsync(cancellationToken);
        if (phoneNumberId is null)
        {
            await FailAsync(message, "WhatsApp отключён или не настроен.", cancellationToken);
            return;
        }

        var parameters = (message.ParameterNames ?? [])
            .Select(n => new KeyValuePair<string, string>(n, message.Parameters?.GetValueOrDefault(n) ?? string.Empty))
            .ToList();
        var result = await client.SendAsync(
            new WhatsAppOutgoing(phoneNumberId, message.Recipient, message.WhatsAppTemplate, message.WhatsAppLanguage, parameters, message.IsAuthentication,
                message.WhatsAppTemplate is null ? message.Body : null),
            cancellationToken);
        var now = clock.UtcNow;
        if (result.Success)
        {
            message.MarkSent(result.MessageId, now);
            message.ForgetSecrets();
            if (message.ConversationId is { } conversationId && await db.Set<Conversation>().FindAsync([conversationId], cancellationToken) is { } conversation)
            {
                conversation.RegisterOutbound(now);
            }

            return;
        }

        if (result.Retryable)
        {
            if (message.RecordTemporaryFailure(result.Error ?? "Временная ошибка WhatsApp.", now))
            {
                message.ForgetSecrets();
                await dispatcher.EmailFallbackAsync(message, cancellationToken);
                return;
            }

            throw new TemporaryDeliveryException(result.Error ?? "Временная ошибка WhatsApp.");
        }

        await FailAsync(message, result.Error ?? "Ошибка WhatsApp.", cancellationToken);
        LogWhatsAppFailed(logger, message.Id, result.ErrorCode);
    }

    private async Task SendEmailAsync(Message message, CancellationToken ct)
    {
        try
        {
            var content = new EmailMessage(message.Recipient!, message.Subject ?? string.Empty, TemplateText.ToHtml(message.Body), message.Body);
            await email.SendAsync(await composer.ComposeAsync(content, ct), ct);
            message.MarkSent(null, clock.UtcNow);
        }
        catch (PermanentOutboxException ex)
        {
            message.MarkFailed(ex.Message, clock.UtcNow);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (!message.RecordTemporaryFailure($"Почтовый сервер: {ex.Message}", clock.UtcNow))
            {
                throw new TemporaryDeliveryException(ex.Message);
            }
        }
    }

    private async Task FailAsync(Message message, string error, CancellationToken ct)
    {
        message.MarkFailed(error, clock.UtcNow);
        message.ForgetSecrets();
        await dispatcher.EmailFallbackAsync(message, ct);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "WhatsApp: сообщение {MessageId} не доставлено, код {Code}")]
    private static partial void LogWhatsAppFailed(ILogger logger, Guid messageId, int? code);
}
