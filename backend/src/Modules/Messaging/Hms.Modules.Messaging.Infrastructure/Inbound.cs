using System.Globalization;
using System.Text.Json;
using Hms.Modules.Guests.Contracts;
using Hms.Modules.Hotel.Contracts;
using Hms.Modules.Messaging.Domain;
using Hms.Modules.Rooms.Contracts;
using Hms.Platform.Outbox;
using Hms.Platform.Time;
using Hms.Platform.Webhooks;
using Hms.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hms.Modules.Messaging.Infrastructure;

/// <summary>Разобранные из вебхука Meta входящее сообщение и статус доставки.</summary>
internal sealed record InboundMessage(string From, string Id, DateTimeOffset At, string Text, string? ProfileName);

internal sealed record DeliveryStatus(string MessageId, MessageStatus Status, DateTimeOffset At, string? Error, int? ErrorCode);

internal sealed record WebhookBatch(string? PhoneNumberId, IReadOnlyList<InboundMessage> Messages, IReadOnlyList<DeliveryStatus> Statuses);

/// <summary>
/// Разбор тела вебхука WhatsApp (документация Meta, Webhooks payload examples):
/// <c>entry[].changes[].value</c> с <c>metadata.phone_number_id</c>, <c>messages[]</c>, <c>contacts[]</c>, <c>statuses[]</c>.
/// </summary>
internal static class WhatsAppWebhookParser
{
    public static IReadOnlyList<WebhookBatch> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new List<WebhookBatch>();
        if (!doc.RootElement.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var change in changes.EnumerateArray())
            {
                if (Str(change, "field") != "messages" || !change.TryGetProperty("value", out var value))
                {
                    continue;
                }

                var phoneNumberId = value.TryGetProperty("metadata", out var meta) ? Str(meta, "phone_number_id") : null;
                var names = new Dictionary<string, string>(StringComparer.Ordinal);
                if (value.TryGetProperty("contacts", out var contacts) && contacts.ValueKind == JsonValueKind.Array)
                {
                    foreach (var c in contacts.EnumerateArray())
                    {
                        if (Str(c, "wa_id") is { } waId && c.TryGetProperty("profile", out var profile) && Str(profile, "name") is { } name)
                        {
                            names[waId] = name;
                        }
                    }
                }

                var messages = new List<InboundMessage>();
                if (value.TryGetProperty("messages", out var ms) && ms.ValueKind == JsonValueKind.Array)
                {
                    foreach (var m in ms.EnumerateArray())
                    {
                        // Номер отправителя может отсутствовать у пользователей с именем пользователя (BSUID) — такие пока не обрабатываем.
                        if (Str(m, "from") is not { } from || Str(m, "id") is not { } id)
                        {
                            continue;
                        }

                        messages.Add(new InboundMessage(from, id, Time(Str(m, "timestamp")), TextOf(m), names.GetValueOrDefault(from)));
                    }
                }

                var statuses = new List<DeliveryStatus>();
                if (value.TryGetProperty("statuses", out var ss) && ss.ValueKind == JsonValueKind.Array)
                {
                    foreach (var s in ss.EnumerateArray())
                    {
                        if (Str(s, "id") is not { } id || StatusOf(Str(s, "status")) is not { } status)
                        {
                            continue;
                        }

                        string? error = null;
                        int? code = null;
                        if (s.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
                        {
                            var e = errors[0];
                            code = e.TryGetProperty("code", out var c) && c.TryGetInt32(out var v) ? v : null;
                            var details = e.TryGetProperty("error_data", out var d) ? Str(d, "details") : null;
                            error = WhatsAppErrorCodes.Describe(code, details ?? Str(e, "message") ?? Str(e, "title"));
                        }

                        statuses.Add(new DeliveryStatus(id, status, Time(Str(s, "timestamp")), error, code));
                    }
                }

                result.Add(new WebhookBatch(phoneNumberId, messages, statuses));
            }
        }

        return result;
    }

    private static string TextOf(JsonElement m) => Str(m, "type") switch
    {
        "text" when m.TryGetProperty("text", out var t) => Str(t, "body") ?? string.Empty,
        "button" when m.TryGetProperty("button", out var b) => Str(b, "text") ?? string.Empty,
        "interactive" when m.TryGetProperty("interactive", out var i) =>
            (i.TryGetProperty("button_reply", out var br) ? Str(br, "title") : i.TryGetProperty("list_reply", out var lr) ? Str(lr, "title") : null) ?? string.Empty,
        { } type => $"[{TypeName(type)}]",
        null => "[сообщение]",
    };

    private static string TypeName(string type) => type switch
    {
        "image" => "фото",
        "audio" => "аудио",
        "video" => "видео",
        "document" => "документ",
        "location" => "геопозиция",
        "sticker" => "стикер",
        "contacts" => "контакт",
        _ => "сообщение",
    };

    private static MessageStatus? StatusOf(string? s) => s switch
    {
        "sent" => MessageStatus.Sent,
        "delivered" => MessageStatus.Delivered,
        "read" => MessageStatus.Read,
        "failed" => MessageStatus.Failed,
        _ => null,
    };

    private static DateTimeOffset Time(string? unixSeconds) =>
        long.TryParse(unixSeconds, NumberStyles.None, CultureInfo.InvariantCulture, out var s) ? DateTimeOffset.FromUnixTimeSeconds(s) : DateTimeOffset.UtcNow;

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>Тексты автоответчика: меню, номера и цены из номерного фонда, правила и время из настроек гостиницы.</summary>
internal sealed class AutoReplyRenderer(MessageVariables variables, IRoomDirectory rooms)
{
    private const int MaxTypes = 15;

    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public async Task<Dictionary<string, string>> ValuesAsync(IReadOnlyList<AutoReply> items, CancellationToken ct)
    {
        var p = await variables.ProfileAsync(ct);
        var sellable = await rooms.ListSellableAsync(null, ct);
        var all = sellable.GroupBy(r => (r.RoomTypeId, r.RoomTypeName)).OrderBy(g => g.Key.RoomTypeName, StringComparer.Ordinal).ToList();
        // Сообщение должно оставаться коротким: не больше 15 категорий, остальные — на сайте.
        var types = all.Take(MaxTypes).ToList();
        var more = all.Count > MaxTypes ? $"\nИ ещё {all.Count - MaxTypes} — на сайте: {variables.SiteUrl}" : string.Empty;
        var values = await variables.HotelAsync(ct);
        values["check_in_time"] = p.CheckInTime.ToString("HH:mm", Ru);
        values["check_out_time"] = p.CheckOutTime.ToString("HH:mm", Ru);
        values["hourly_min_hours"] = p.HourlyMinHours.ToString(Ru);
        values["rules"] = string.IsNullOrWhiteSpace(p.Rules) ? $"Правила гостиницы — на сайте: {variables.SiteUrl}" : p.Rules;
        values["rooms_list"] = types.Count == 0
            ? "Сейчас номеров в продаже нет."
            : string.Join('\n', types.Select(g => $"• {g.Key.RoomTypeName} — до {g.Max(r => r.Capacity)} гостей")) + more;
        values["price_list"] = types.Count == 0
            ? "Цены уточняйте у администратора."
            : string.Join('\n', types.Select(g =>
            {
                var night = g.Min(r => r.PriceOverride ?? r.BasePrice);
                var hour = g.Where(r => r.SellsHourly).Select(r => r.HourlyPriceOverride ?? r.HourlyBasePrice!.Value).DefaultIfEmpty().Min();
                return hour > 0
                    ? $"• {g.Key.RoomTypeName} — от {TemplateText.Money(night)} за ночь, от {TemplateText.Money(hour)} за час"
                    : $"• {g.Key.RoomTypeName} — от {TemplateText.Money(night)} за ночь";
            })) + more;
        values["menu"] = AutoReplyMatcher.Menu(items);
        return values;
    }
}

/// <summary>Что ответить на входящее сообщение (меню/FAQ без ИИ). Null — молчать (диалог у администратора, автоответчик выключен).</summary>
internal sealed record AutoReplyDecision(string Reply, string Outcome, bool RevokeConsent, bool HandOff);

internal sealed class AutoResponder(DbContext db, AutoReplyRenderer renderer)
{
    public async Task<AutoReplyDecision?> DecideAsync(Guid hotelId, string text, bool firstContact, bool withOperator, CancellationToken ct)
    {
        var settings = await db.Set<MessagingSettings>().AsNoTracking().FirstOrDefaultAsync(s => s.HotelId == hotelId, ct);
        if (settings is null)
        {
            return null;
        }

        var items = await db.Set<AutoReply>().AsNoTracking().Where(a => a.HotelId == hotelId).OrderBy(a => a.Number).ToListAsync(ct);
        var values = await renderer.ValuesAsync(items, ct);
        string Render(string t) => TemplateText.Render(t, values);

        // Отписку соблюдаем всегда, даже если автоответчик выключен (правила WhatsApp Business).
        if (AutoReplyMatcher.IsStop(text))
        {
            return new AutoReplyDecision(Render(settings.StopText), "stop", RevokeConsent: true, HandOff: false);
        }

        if (!settings.AutoRepliesEnabled || (withOperator && !AutoReplyMatcher.IsMenu(text)))
        {
            return null;
        }

        if (AutoReplyMatcher.IsMenu(text))
        {
            return new AutoReplyDecision(Render(settings.GreetingText), "menu", false, false);
        }

        if (AutoReplyMatcher.Match(text, items) is { } item)
        {
            return new AutoReplyDecision(Render(item.Body), item.Action == AutoReplyAction.Operator ? "operator" : $"item:{item.Number}", false, item.Action == AutoReplyAction.Operator);
        }

        return firstContact
            ? new AutoReplyDecision(Render(settings.GreetingText), "greeting", false, false)
            : new AutoReplyDecision(Render(settings.FallbackText), "fallback", false, false);
    }
}

/// <summary>
/// Обработка события вебхука WhatsApp из outbox: статусы доставки, входящие сообщения, автоответы и отписка.
/// Повтор события безопасен: входящее отсекается по wamid, статус только растёт.
/// </summary>
internal sealed partial class WhatsAppWebhookHandler(
    DbContext db,
    IHotelContext hotel,
    IHotelProfile hotelProfile,
    IGuestMessaging guests,
    ConversationRegistry conversations,
    AutoResponder responder,
    NotificationDispatcher dispatcher,
    IOutbox outbox,
    IHotelClock clock,
    ILogger<WhatsAppWebhookHandler> logger) : OutboxHandler<WebhookEventRef>
{
    public override string Type => MessagingOutbox.WhatsAppWebhook;

    protected override async Task HandleAsync(WebhookEventRef payload, CancellationToken cancellationToken)
    {
        var evt = await db.Set<WebhookEvent>().FirstOrDefaultAsync(e => e.Id == payload.EventId, cancellationToken);
        if (evt is null || evt.ProcessedAt is not null)
        {
            return;
        }

        IReadOnlyList<WebhookBatch> batches;
        try
        {
            batches = WhatsAppWebhookParser.Parse(evt.Payload);
        }
        catch (JsonException ex)
        {
            evt.MarkProcessed(clock.UtcNow, $"Некорректный JSON: {ex.Message}");
            return;
        }

        var hotelId = await hotel.GetHotelIdAsync(cancellationToken);
        var ours = (await hotelProfile.GetWhatsAppAsync(cancellationToken)).PhoneNumberId;
        foreach (var batch in batches)
        {
            // События другого номера (тот же App у нескольких номеров) не наши.
            if (ours is not null && batch.PhoneNumberId is not null && batch.PhoneNumberId != ours)
            {
                continue;
            }

            foreach (var status in batch.Statuses)
            {
                await ApplyStatusAsync(hotelId, status, cancellationToken);
            }

            foreach (var message in batch.Messages)
            {
                await ReceiveAsync(hotelId, message, cancellationToken);
            }
        }

        evt.MarkProcessed(clock.UtcNow);
    }

    private async Task ApplyStatusAsync(Guid hotelId, DeliveryStatus status, CancellationToken ct)
    {
        var message = await db.Set<Message>().FirstOrDefaultAsync(m => m.HotelId == hotelId && m.ExternalId == status.MessageId, ct);
        if (message is null || !message.ApplyDeliveryStatus(status.Status, status.At, status.Error))
        {
            return;
        }

        if (status.Status == MessageStatus.Failed)
        {
            LogStatusFailed(logger, message.Id, status.ErrorCode);
            await dispatcher.EmailFallbackAsync(message, ct);
        }
    }

    private async Task ReceiveAsync(Guid hotelId, InboundMessage inbound, CancellationToken ct)
    {
        if (await db.Set<Message>().AnyAsync(m => m.HotelId == hotelId && m.ExternalId == inbound.Id, ct))
        {
            return;
        }

        var parsed = PhoneNumber.Parse("+" + inbound.From.TrimStart('+'));
        if (parsed.IsFailure)
        {
            return;
        }

        var phone = parsed.Value.Value;
        var guest = await guests.FindByWhatsAppAsync(phone, ct);
        var conversation = await conversations.GetOrCreateAsync(hotelId, phone, guest?.Id, inbound.At, ct);
        var first = conversation.RegisterInbound(guest?.Id, inbound.ProfileName, inbound.At);
        var incoming = Message.Incoming(hotelId, guest?.Id, phone, inbound.Text, inbound.Id, inbound.At);
        incoming.AttachConversation(conversation.Id);
        db.Set<Message>().Add(incoming);

        var decision = await responder.DecideAsync(hotelId, inbound.Text, first, conversation.NeedsOperator, ct);
        if (decision is null)
        {
            return;
        }

        if (decision.RevokeConsent && guest is not null)
        {
            await guests.RevokeWhatsAppConsentAsync(guest.Id, ct);
        }

        if (decision.HandOff)
        {
            conversation.RequestOperator();
        }

        // Ответ на сообщение гостя — сервисное сообщение в окне обслуживания (свободный текст).
        var reply = Message.WhatsAppText(hotelId, guest?.Id, null, MessageKind.AutoReply, phone, decision.Reply, sentBy: null, clock.UtcNow);
        reply.AttachConversation(conversation.Id);
        db.Set<Message>().Add(reply);
        outbox.Enqueue(MessagingOutbox.Deliver, new DeliverPayload(reply.Id));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "WhatsApp: статус «не доставлено» для сообщения {MessageId}, код {Code}")]
    private static partial void LogStatusFailed(ILogger logger, Guid messageId, int? code);
}
