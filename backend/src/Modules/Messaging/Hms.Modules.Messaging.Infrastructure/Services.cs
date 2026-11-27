using System.Globalization;
using Hms.Modules.Audit.Contracts;
using Hms.Modules.Bookings.Contracts;
using Hms.Modules.Guests.Contracts;
using Hms.Modules.Hotel.Contracts;
using Hms.Modules.Identity.Contracts;
using Hms.Modules.Messaging.Application;
using Hms.Modules.Messaging.Domain;
using Hms.Platform.Outbox;
using Hms.Platform.Security;
using Hms.Platform.Time;
using Hms.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Hms.Modules.Messaging.Infrastructure;

/// <summary>История сообщений, ручная отправка администратором и повтор (ТЗ §11).</summary>
internal sealed class MessageService(
    DbContext db,
    IHotelContext hotel,
    IGuestMessaging guests,
    IBookingCharges bookings,
    IUserDirectory users,
    MessageVariables variables,
    WhatsAppGateway whatsApp,
    ConversationRegistry conversations,
    IOutbox outbox,
    ICurrentUser currentUser,
    IAuditTrail audit,
    IHotelClock clock) : IMessageService
{
    public async Task<PagedResult<MessageDto>> ListAsync(MessageQuery q, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var page = new PageRequest(q.Page, q.PageSize);
        var query = db.Set<Message>().AsNoTracking().Where(m => m.HotelId == hotelId);
        if (q.GuestId is { } guestId)
        {
            query = query.Where(m => m.GuestId == guestId);
        }

        if (q.BookingId is { } bookingId)
        {
            query = query.Where(m => m.BookingId == bookingId);
        }

        if (q.ConversationId is { } conversationId)
        {
            query = query.Where(m => m.ConversationId == conversationId);
        }

        if (Enum.TryParse<MessageChannel>(q.Channel, out var channel))
        {
            query = query.Where(m => m.Channel == channel);
        }

        if (Enum.TryParse<MessageStatus>(q.Status, out var status))
        {
            query = query.Where(m => m.Status == status);
        }

        if (Enum.TryParse<MessageKind>(q.Kind, out var kind))
        {
            query = query.Where(m => m.Kind == kind);
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        return new PagedResult<MessageDto>(await ToDtosAsync(items, ct), page.Page, page.PageSize, total);
    }

    public async Task<Result<SendOptionsDto>> SendOptionsAsync(Guid? guestId, Guid? conversationId, CancellationToken ct)
    {
        var target = await ResolveAsync(guestId, conversationId, ct);
        if (target.IsFailure)
        {
            return target.Error;
        }

        var (guest, conversation, phone) = target.Value;
        var available = await whatsApp.IsAvailableAsync(ct);
        var now = clock.UtcNow;
        var windowOpen = conversation?.IsWindowOpen(now) == true;
        var manual = await ManualTemplateAsync(ct);
        var consent = guest?.WhatsAppConsent == true;
        string? block = !available ? "WhatsApp не подключён."
            : phone is null ? "У гостя не указан телефон WhatsApp."
            : windowOpen ? null
            : !consent ? "Гость не давал согласия на сообщения в WhatsApp и не писал последние 24 часа."
            : manual?.WhatsAppReady != true ? "Гость не писал последние 24 часа, а шаблон «Сообщение от администратора» ещё не одобрен в Meta."
            : null;
        return new SendOptionsDto(guest?.Email is not null, block is null, consent, windowOpen, conversation?.WindowClosesAt, manual?.WhatsAppReady == true, block);
    }

    public async Task<Result<MessageDto>> SendAsync(SendMessageRequest r, CancellationToken ct)
    {
        var target = await ResolveAsync(r.GuestId, r.ConversationId, ct);
        if (target.IsFailure)
        {
            return target.Error;
        }

        var (guest, conversation, phone) = target.Value;
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var now = clock.UtcNow;
        var text = r.Text.Trim();
        var template = await ManualTemplateAsync(ct) ?? throw new InvalidOperationException("Нет шаблона manual_notice — выполните migrate.");
        var values = await variables.HotelAsync(ct);
        values["guest_name"] = guest?.FullName ?? conversation?.ProfileName ?? "гость";
        values["text"] = text;

        Message message;
        if (Enum.Parse<MessageChannel>(r.Channel) == MessageChannel.Email)
        {
            if (guest?.Email is not { } email)
            {
                return MessagingAppErrors.NoEmail;
            }

            var subject = string.IsNullOrWhiteSpace(r.Subject) ? TemplateText.Render(template.EmailSubject, values) : r.Subject.Trim();
            message = Message.Email(hotelId, guest.Id, r.BookingId, MessageKind.Manual, TemplateCatalog.ManualNotice, email, subject,
                TemplateText.Render(template.EmailBody, values), dedupKey: null, currentUser.UserId, now);
        }
        else
        {
            if (!await whatsApp.IsAvailableAsync(ct))
            {
                return Hms.Modules.Messaging.Contracts.MessagingErrors.WhatsAppUnavailable;
            }

            if (phone is null)
            {
                return MessagingAppErrors.NoPhone;
            }

            if (conversation?.IsWindowOpen(now) == true)
            {
                // Окно обслуживания открыто (гость писал в последние 24 часа) — свободный текст.
                message = Message.WhatsAppText(hotelId, guest?.Id ?? conversation.GuestId, r.BookingId, MessageKind.Manual, phone, text, currentUser.UserId, now);
            }
            else if (guest?.WhatsAppConsent != true)
            {
                return MessagingAppErrors.NoConsent;
            }
            else if (!template.WhatsAppReady)
            {
                return MessagingAppErrors.OutsideWindow;
            }
            else
            {
                var names = TemplateText.Placeholders(template.WhatsAppBody).ToArray();
                message = Message.WhatsAppTemplateMessage(hotelId, guest.Id, r.BookingId, MessageKind.Manual, TemplateCatalog.ManualNotice, phone,
                    TemplateText.Render(template.WhatsAppBody, values), template.WhatsAppName!, template.Language, values, names, isAuthentication: false,
                    emailFallback: false, dedupKey: null, currentUser.UserId, now);
            }

            conversation ??= await conversations.GetOrCreateAsync(hotelId, phone, guest?.Id, now, ct);
            message.AttachConversation(conversation.Id);
            conversation.MarkRead();
        }

        db.Set<Message>().Add(message);
        outbox.Enqueue(MessagingOutbox.Deliver, new DeliverPayload(message.Id));
        // Без текста и адреса (ПД): только канал и получатель-карточка.
        audit.Record(new AuditEntry("message.sent", "Message", message.Id.ToString(), After: new { Channel = message.Channel.ToString(), message.GuestId, message.BookingId }));
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([message], ct))[0];
    }

    public async Task<Result<MessageDto>> RetryAsync(Guid id, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var message = await db.Set<Message>().FirstOrDefaultAsync(m => m.Id == id && m.HotelId == hotelId, ct);
        if (message is null)
        {
            return MessagingAppErrors.NotFound;
        }

        var retried = message.Retry(clock.UtcNow);
        if (retried.IsFailure)
        {
            return retried.Error;
        }

        outbox.Enqueue(MessagingOutbox.Deliver, new DeliverPayload(message.Id));
        audit.Record(new AuditEntry("message.retried", "Message", id.ToString(), After: new { Channel = message.Channel.ToString() }));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return MessagingAppErrors.ConcurrencyConflict;
        }

        return (await ToDtosAsync([message], ct))[0];
    }

    private async Task<MessageTemplate?> ManualTemplateAsync(CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        return await db.Set<MessageTemplate>().AsNoTracking()
            .FirstOrDefaultAsync(t => t.HotelId == hotelId && t.Code == TemplateCatalog.ManualNotice && t.Language == TemplateCatalog.DefaultLanguage, ct);
    }

    /// <summary>Получатель: карточка гостя и/или диалог WhatsApp. Телефон — из диалога, иначе из карточки.</summary>
    private async Task<Result<(GuestMessagingProfile? Guest, Conversation? Conversation, string? Phone)>> ResolveAsync(Guid? guestId, Guid? conversationId, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        Conversation? conversation = null;
        if (conversationId is { } cid)
        {
            conversation = await db.Set<Conversation>().FirstOrDefaultAsync(c => c.Id == cid && c.HotelId == hotelId, ct);
            if (conversation is null)
            {
                return MessagingAppErrors.ConversationNotFound;
            }
        }

        var gid = guestId ?? conversation?.GuestId;
        GuestMessagingProfile? guest = null;
        if (gid is { } g)
        {
            guest = await guests.GetAsync(g, ct);
            if (guest is null || guest.IsAnonymized)
            {
                return MessagingAppErrors.GuestNotFound;
            }
        }

        if (guest is null && conversation is null)
        {
            return MessagingAppErrors.RecipientRequired;
        }

        var phone = conversation?.Phone ?? guest?.WhatsAppNumber;
        if (conversation is null && phone is not null)
        {
            conversation = await db.Set<Conversation>().FirstOrDefaultAsync(c => c.HotelId == hotelId && c.Phone == phone, ct);
        }

        return (guest, conversation, phone);
    }

    private async Task<IReadOnlyList<MessageDto>> ToDtosAsync(IReadOnlyList<Message> items, CancellationToken ct)
    {
        var guestIds = items.Where(m => m.GuestId is not null).Select(m => m.GuestId!.Value).Distinct().ToList();
        var guestNames = guestIds.Count == 0 ? new Dictionary<Guid, GuestMessagingProfile>() : await guests.GetManyAsync(guestIds, ct);
        var bookingIds = items.Where(m => m.BookingId is not null).Select(m => m.BookingId!.Value).Distinct().ToList();
        var numbers = bookingIds.Count == 0 ? [] : (await bookings.ListAsync(bookingIds, ct)).ToDictionary(b => b.BookingId, b => b.Number);
        var staff = await users.GetNamesAsync(items.Where(m => m.SentBy is not null).Select(m => m.SentBy!.Value).Distinct(), ct);
        return items.Select(m => new MessageDto(
            m.Id,
            m.GuestId,
            m.GuestId is { } g ? guestNames.GetValueOrDefault(g)?.FullName : null,
            m.BookingId,
            m.BookingId is { } b ? numbers.GetValueOrDefault(b) : null,
            m.Channel.ToString(),
            m.Direction.ToString(),
            m.Kind.ToString(),
            m.TemplateCode,
            m.TemplateCode is { } code ? TemplateCatalog.Find(code)?.Title : null,
            m.Recipient,
            m.Subject,
            m.Body,
            m.Status.ToString(),
            m.Error,
            m.CreatedAt,
            m.SentAt,
            m.DeliveredAt,
            m.ReadAt,
            m.SentBy is { } s ? staff.GetValueOrDefault(s) : null,
            m.FallbackFor,
            m.Direction == MessageDirection.Out && m.Status == MessageStatus.Failed && m.Recipient is not null && m.Kind != MessageKind.VerificationCode)).ToList();
    }
}

/// <summary>Диалоги WhatsApp: список, прочтение, передача администратору.</summary>
internal sealed class ConversationService(DbContext db, IHotelContext hotel, IGuestMessaging guests, IAuditTrail audit, IHotelClock clock) : IConversationService
{
    public async Task<PagedResult<ConversationDto>> ListAsync(ConversationQuery q, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var page = new PageRequest(q.Page, q.PageSize);
        var query = db.Set<Conversation>().AsNoTracking().Where(c => c.HotelId == hotelId && !c.Phone.StartsWith("deleted:"));
        if (q.NeedsOperator == true)
        {
            query = query.Where(c => c.NeedsOperator);
        }

        if (PhoneNumber.SearchDigits(q.Search) is { } digits)
        {
            query = query.Where(c => c.Phone.Contains(digits));
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(c => c.NeedsOperator).ThenByDescending(c => c.LastMessageAt).Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        return new PagedResult<ConversationDto>(await ToDtosAsync(items, ct), page.Page, page.PageSize, total);
    }

    public async Task<Result<ConversationDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var c = await LoadAsync(id, tracking: false, ct);
        return c is null ? MessagingAppErrors.ConversationNotFound : (await ToDtosAsync([c], ct))[0];
    }

    public Task<Result<ConversationDto>> MarkReadAsync(Guid id, CancellationToken ct) => ChangeAsync(id, c => c.MarkRead(), null, ct);

    public Task<Result<ConversationDto>> ResolveAsync(Guid id, CancellationToken ct) => ChangeAsync(id, c => c.Resolve(), "conversation.resolved", ct);

    public async Task<int> NeedsOperatorCountAsync(CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        return await db.Set<Conversation>().CountAsync(c => c.HotelId == hotelId && c.NeedsOperator, ct);
    }

    private async Task<Result<ConversationDto>> ChangeAsync(Guid id, Action<Conversation> change, string? auditAction, CancellationToken ct)
    {
        var c = await LoadAsync(id, tracking: true, ct);
        if (c is null)
        {
            return MessagingAppErrors.ConversationNotFound;
        }

        change(c);
        if (auditAction is not null)
        {
            audit.Record(new AuditEntry(auditAction, "Conversation", id.ToString()));
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Пришло новое сообщение гостя — изменение не страшно повторить.
            db.ChangeTracker.Clear();
            return MessagingAppErrors.ConcurrencyConflict;
        }

        return (await ToDtosAsync([c], ct))[0];
    }

    private async Task<Conversation?> LoadAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var query = db.Set<Conversation>().Where(c => c.Id == id && c.HotelId == hotelId && !c.Phone.StartsWith("deleted:"));
        return await (tracking ? query : query.AsNoTracking()).FirstOrDefaultAsync(ct);
    }

    private async Task<IReadOnlyList<ConversationDto>> ToDtosAsync(IReadOnlyList<Conversation> items, CancellationToken ct)
    {
        var ids = items.Select(c => c.Id).ToList();
        var last = await db.Set<Message>().AsNoTracking()
            .Where(m => m.ConversationId != null && ids.Contains(m.ConversationId.Value))
            .GroupBy(m => m.ConversationId!.Value)
            .Select(g => g.OrderByDescending(m => m.CreatedAt).Select(m => new { ConversationId = m.ConversationId!.Value, m.Body, m.Direction }).First())
            .ToListAsync(ct);
        var preview = last.ToDictionary(x => x.ConversationId, x => (x.Direction == MessageDirection.Out ? "Вы: " : string.Empty) + Short(x.Body));
        var guestIds = items.Where(c => c.GuestId is not null).Select(c => c.GuestId!.Value).Distinct().ToList();
        var profiles = guestIds.Count == 0 ? new Dictionary<Guid, GuestMessagingProfile>() : await guests.GetManyAsync(guestIds, ct);
        var now = clock.UtcNow;
        return items.Select(c =>
        {
            var guest = c.GuestId is { } g ? profiles.GetValueOrDefault(g) : null;
            return new ConversationDto(c.Id, c.Phone, c.GuestId, guest?.FullName, c.ProfileName, c.LastMessageAt, c.LastInboundAt, c.IsWindowOpen(now),
                c.WindowClosesAt, c.NeedsOperator, c.UnreadCount, preview.GetValueOrDefault(c.Id), guest?.WhatsAppConsent == true);
        }).ToList();
    }

    private static string Short(string text) => text.Length > 80 ? text[..80] + "…" : text;
}

/// <summary>Шаблоны сообщений: набор фиксирован каталогом, владелец правит тексты и статус модерации Meta.</summary>
internal sealed class TemplateService(DbContext db, IHotelContext hotel, ICurrentUser currentUser, IAuditTrail audit, IHotelClock clock) : ITemplateService
{
    private static readonly Dictionary<string, string> Sample = new(StringComparer.Ordinal)
    {
        ["hotel_name"] = "Гостиница «Пример»",
        ["hotel_contacts"] = "Тел.: +996 555 000 000\nул. Примерная, 1",
        ["site_url"] = "https://example.com",
        ["guest_name"] = "Айгуль",
        ["booking_number"] = "Ч-2811-01",
        ["starts_at"] = "28.11.2026 15:00",
        ["ends_at"] = "28.11.2026 18:00",
        ["stay_info"] = "Время: 28.11.2026 с 15:00 до 18:00.",
        ["room_number"] = "101",
        ["room_type"] = "Стандарт",
        ["total_amount"] = "1 500 сом",
        ["booking_url"] = "https://example.com/account/bookings/…",
        ["amount"] = "1 000 сом",
        ["cancel_info"] = "Штраф по правилам отмены: 500 сом.",
        ["text"] = "Ваш номер готов, можно заселяться.",
        ["code"] = "123456",
    };

    public async Task<IReadOnlyList<TemplateDto>> ListAsync(CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var rows = await db.Set<MessageTemplate>().AsNoTracking().Where(t => t.HotelId == hotelId).ToListAsync(ct);
        var order = TemplateCatalog.All.Select((d, i) => (d.Code, i)).ToDictionary(x => x.Code, x => x.i, StringComparer.Ordinal);
        return rows.OrderBy(t => order.GetValueOrDefault(t.Code, int.MaxValue)).ThenBy(t => t.Language, StringComparer.Ordinal).Select(ToDto).ToList();
    }

    public async Task<Result<TemplateDto>> UpdateAsync(string code, string language, UpdateTemplateRequest r, CancellationToken ct)
    {
        var template = await LoadAsync(code, language, ct);
        if (template is null)
        {
            return MessagingAppErrors.TemplateNotFound;
        }

        if (r.Version is { } version)
        {
            db.Entry(template).Property(t => t.Version).OriginalValue = version;
        }

        var result = template.Update(r.EmailSubject, r.EmailBody, r.WhatsAppBody, r.WhatsAppName, Enum.Parse<WhatsAppTemplateStatus>(r.WhatsAppStatus), r.IsActive,
            currentUser.UserId, clock.UtcNow);
        if (result.IsFailure)
        {
            return result.Error;
        }

        audit.Record(new AuditEntry("template.updated", "MessageTemplate", $"{code}:{language}", After: new { r.IsActive, r.WhatsAppStatus, r.WhatsAppName }));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return MessagingAppErrors.ConcurrencyConflict;
        }

        return ToDto(template);
    }

    public async Task<Result<TemplatePreviewDto>> PreviewAsync(string code, string language, CancellationToken ct)
    {
        var template = await LoadAsync(code, language, ct);
        return template is null
            ? MessagingAppErrors.TemplateNotFound
            : new TemplatePreviewDto(TemplateText.Render(template.EmailSubject, Sample), TemplateText.Render(template.EmailBody, Sample), TemplateText.Render(template.WhatsAppBody, Sample));
    }

    private async Task<MessageTemplate?> LoadAsync(string code, string language, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        return await db.Set<MessageTemplate>().FirstOrDefaultAsync(t => t.HotelId == hotelId && t.Code == code && t.Language == language, ct);
    }

    private static TemplateDto ToDto(MessageTemplate t)
    {
        var d = TemplateCatalog.Find(t.Code)!;
        return new TemplateDto(t.Code, t.Language, d.Title, d.Event?.ToString(), d.EmailAllowed, d.IsAuthentication, d.Variables, t.EmailSubject, t.EmailBody,
            t.WhatsAppName, t.WhatsAppBody, TemplateText.Placeholders(t.WhatsAppBody), t.WhatsAppStatus.ToString(), t.IsActive, t.UpdatedAt, t.Version);
    }
}

/// <summary>Настройки напоминаний и автоответчика (владелец).</summary>
internal sealed class MessagingSettingsService(
    DbContext db,
    IHotelContext hotel,
    WhatsAppGateway whatsApp,
    AutoResponder responder,
    IAuditTrail audit,
    IHotelClock clock) : IMessagingSettingsService
{
    public async Task<MessagingSettingsDto> GetAsync(CancellationToken ct) => await ToDtoAsync(await LoadAsync(tracking: false, ct), ct);

    public async Task<Result<MessagingSettingsDto>> UpdateAsync(UpdateMessagingSettingsRequest r, CancellationToken ct)
    {
        var settings = await LoadAsync(tracking: true, ct);
        if (r.Version is { } version)
        {
            db.Entry(settings).Property(s => s.Version).OriginalValue = version;
        }

        var result = settings.Update(r.HourlyStartReminderMinutes, r.HourlyEndReminderMinutes, TimeOnly.Parse(r.NightlyStartReminderTime, CultureInfo.InvariantCulture),
            r.NightlyEndReminderMinutes, r.AutoRepliesEnabled, r.GreetingText, r.FallbackText, r.StopText, clock.UtcNow);
        if (result.IsFailure)
        {
            return result.Error;
        }

        audit.Record(new AuditEntry("messaging.settings.updated", "MessagingSettings", settings.HotelId.ToString(),
            After: new { r.HourlyStartReminderMinutes, r.HourlyEndReminderMinutes, r.NightlyStartReminderTime, r.NightlyEndReminderMinutes, r.AutoRepliesEnabled }));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return MessagingAppErrors.ConcurrencyConflict;
        }

        return await ToDtoAsync(settings, ct);
    }

    public async Task<AutoRepliesDto> GetAutoRepliesAsync(CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var items = await db.Set<AutoReply>().AsNoTracking().Where(a => a.HotelId == hotelId).OrderBy(a => a.Number).ToListAsync(ct);
        return new AutoRepliesDto(items.Select(ToDto).ToList(), AutoReplyVariables.All);
    }

    /// <summary>Меню сохраняется целиком: старые пункты заменяются новыми одной транзакцией.</summary>
    public async Task<Result<AutoRepliesDto>> SaveAutoRepliesAsync(SaveAutoRepliesRequest r, CancellationToken ct)
    {
        foreach (var item in r.Items)
        {
            if (TemplateText.Validate(item.Body, AutoReplyVariables.All, $"Пункт {item.Number}") is { } error)
            {
                return error;
            }
        }

        var hotelId = await hotel.GetHotelIdAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Set<AutoReply>().Where(a => a.HotelId == hotelId).ExecuteDeleteAsync(ct);
        db.Set<AutoReply>().AddRange(r.Items.Select(i =>
            AutoReply.Create(hotelId, i.Number, i.Title, [.. i.Keywords ?? []], i.Body, Enum.Parse<AutoReplyAction>(i.Action), i.IsActive)));
        audit.Record(new AuditEntry("autoreplies.updated", "AutoReplies", hotelId.ToString(), After: new { Items = r.Items.Count }));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await GetAutoRepliesAsync(ct);
    }

    public async Task<AutoReplyTestDto> TestAutoReplyAsync(AutoReplyTestRequest r, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var decision = await responder.DecideAsync(hotelId, r.Text, r.FirstContact, withOperator: false, ct);
        return decision is null ? new AutoReplyTestDto(string.Empty, "silent") : new AutoReplyTestDto(decision.Reply, decision.Outcome);
    }

    private async Task<MessagingSettings> LoadAsync(bool tracking, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var query = db.Set<MessagingSettings>().Where(s => s.HotelId == hotelId);
        return await (tracking ? query : query.AsNoTracking()).FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Настройки сообщений не созданы: выполните команду migrate.");
    }

    private async Task<MessagingSettingsDto> ToDtoAsync(MessagingSettings s, CancellationToken ct)
    {
        var (available, availability, phone) = await whatsApp.StateAsync(ct);
        return new MessagingSettingsDto(s.HourlyStartReminderMinutes, s.HourlyEndReminderMinutes, s.NightlyStartReminderTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            s.NightlyEndReminderMinutes, s.AutoRepliesEnabled, s.GreetingText, s.FallbackText, s.StopText,
            new WhatsAppStateDto(available, availability, whatsApp.SecretsConfigured, phone), s.Version);
    }

    private static AutoReplyDto ToDto(AutoReply a) => new(a.Number, a.Title, a.Keywords, a.Body, a.Action.ToString(), a.IsActive);
}
