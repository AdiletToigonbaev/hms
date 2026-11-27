using Hms.SharedKernel;

namespace Hms.Modules.Messaging.Domain;

public enum MessageChannel
{
    Email,
    WhatsApp,
}

public enum MessageDirection
{
    Out,
    In,
}

public enum MessageKind
{
    /// <summary>Автоматическое уведомление о событии (бронь, оплата, напоминание…).</summary>
    Notification,

    /// <summary>Отправил сотрудник вручную.</summary>
    Manual,

    /// <summary>Ответ автоответчика на входящее сообщение.</summary>
    AutoReply,

    /// <summary>Входящее сообщение гостя.</summary>
    Incoming,

    /// <summary>Код подтверждения телефона.</summary>
    VerificationCode,
}

/// <summary>Статусы доставки (ТЗ §11). Queued → Sent → Delivered → Read; Failed — окончательная ошибка; Skipped — не отправлялось (нет канала).</summary>
public enum MessageStatus
{
    Queued,
    Sent,
    Delivered,
    Read,
    Failed,
    Skipped,
    Received,
}

/// <summary>
/// Сообщение в истории (входящее или исходящее, email или WhatsApp). Отправка — через outbox по Id:
/// в outbox нет текста и адреса. Текст и адрес стираются при обезличивании гостя.
/// </summary>
public sealed class Message
{
    public const int MaxAttempts = 8;
    public const int MaxWhatsAppText = 4096;
    public const string AnonymizedText = "[данные гостя удалены]";

    private Message()
    {
        Body = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid HotelId { get; private set; }

    public Guid? GuestId { get; private set; }

    public Guid? BookingId { get; private set; }

    public MessageChannel Channel { get; private set; }

    public MessageDirection Direction { get; private set; }

    public MessageKind Kind { get; private set; }

    public string? TemplateCode { get; private set; }

    /// <summary>Телефон E.164 (WhatsApp) или email. Не логируется.</summary>
    public string? Recipient { get; private set; }

    public string? Subject { get; private set; }

    public string Body { get; private set; }

    /// <summary>Диалог WhatsApp (номер гостя), к которому относится сообщение.</summary>
    public Guid? ConversationId { get; private set; }

    /// <summary>Шаблон Meta и его язык — для отправки шаблоном.</summary>
    public string? WhatsAppTemplate { get; private set; }

    public string? WhatsAppLanguage { get; private set; }

    /// <summary>Переменные уведомления (имя → значение): параметры шаблона WhatsApp и текст письма-замены.</summary>
    public Dictionary<string, string>? Parameters { get; private set; }

    /// <summary>Какие переменные и в каком порядке передать в шаблон WhatsApp.</summary>
    public string[]? ParameterNames { get; private set; }

    /// <summary>Шаблон «Аутентификация»: параметр передаётся позиционно в тексте и в кнопке «Скопировать код».</summary>
    public bool IsAuthentication { get; private set; }

    public MessageStatus Status { get; private set; }

    public string? Error { get; private set; }

    /// <summary>Идентификатор сообщения у провайдера (wamid WhatsApp).</summary>
    public string? ExternalId { get; private set; }

    /// <summary>Ключ дедупликации: одно уведомление о событии — одно сообщение.</summary>
    public string? DedupKey { get; private set; }

    /// <summary>Можно ли при окончательной ошибке WhatsApp отправить то же письмом.</summary>
    public bool EmailFallback { get; private set; }

    /// <summary>Это письмо — замена неотправленному WhatsApp-сообщению.</summary>
    public Guid? FallbackFor { get; private set; }

    public int Attempts { get; private set; }

    public Guid? SentBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public DateTimeOffset? FailedAt { get; private set; }

    public uint Version { get; private set; }

    public static Message Email(Guid hotelId, Guid? guestId, Guid? bookingId, MessageKind kind, string? templateCode, string to, string subject, string body,
        string? dedupKey, Guid? sentBy, DateTimeOffset now, Guid? fallbackFor = null) => new()
        {
            Id = Guid.CreateVersion7(),
            HotelId = hotelId,
            GuestId = guestId,
            BookingId = bookingId,
            Channel = MessageChannel.Email,
            Direction = MessageDirection.Out,
            Kind = kind,
            TemplateCode = templateCode,
            Recipient = to,
            Subject = subject,
            Body = body,
            Status = MessageStatus.Queued,
            DedupKey = dedupKey,
            SentBy = sentBy,
            CreatedAt = now,
            FallbackFor = fallbackFor,
        };

    /// <summary>WhatsApp шаблоном (вне окна обслуживания можно только так).</summary>
    public static Message WhatsAppTemplateMessage(Guid hotelId, Guid? guestId, Guid? bookingId, MessageKind kind, string templateCode, string to, string renderedBody,
        string waTemplate, string language, Dictionary<string, string> parameters, string[] parameterNames, bool isAuthentication, bool emailFallback, string? dedupKey, Guid? sentBy, DateTimeOffset now) => new()
        {
            Id = Guid.CreateVersion7(),
            HotelId = hotelId,
            GuestId = guestId,
            BookingId = bookingId,
            Channel = MessageChannel.WhatsApp,
            Direction = MessageDirection.Out,
            Kind = kind,
            TemplateCode = templateCode,
            Recipient = to,
            Body = renderedBody,
            WhatsAppTemplate = waTemplate,
            WhatsAppLanguage = language,
            Parameters = parameters,
            ParameterNames = parameterNames,
            IsAuthentication = isAuthentication,
            EmailFallback = emailFallback,
            Status = MessageStatus.Queued,
            DedupKey = dedupKey,
            SentBy = sentBy,
            CreatedAt = now,
        };

    /// <summary>Свободный текст WhatsApp — только в окне обслуживания (24 ч после сообщения гостя).</summary>
    public static Message WhatsAppText(Guid hotelId, Guid? guestId, Guid? bookingId, MessageKind kind, string to, string text, Guid? sentBy, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        HotelId = hotelId,
        GuestId = guestId,
        BookingId = bookingId,
        Channel = MessageChannel.WhatsApp,
        Direction = MessageDirection.Out,
        Kind = kind,
        Recipient = to,
        Body = Limit(text),
        Status = MessageStatus.Queued,
        SentBy = sentBy,
        CreatedAt = now,
    };

    public static Message Incoming(Guid hotelId, Guid? guestId, string from, string text, string externalId, DateTimeOffset at) => new()
    {
        Id = Guid.CreateVersion7(),
        HotelId = hotelId,
        GuestId = guestId,
        Channel = MessageChannel.WhatsApp,
        Direction = MessageDirection.In,
        Kind = MessageKind.Incoming,
        Recipient = from,
        Body = text,
        Status = MessageStatus.Received,
        ExternalId = externalId,
        CreatedAt = at,
    };

    /// <summary>Уведомление не отправлялось: нет подходящего канала (нет email и согласия на WhatsApp). Видно в истории.</summary>
    public static Message Skipped(Guid hotelId, Guid? guestId, Guid? bookingId, string templateCode, string reason, string? dedupKey, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        HotelId = hotelId,
        GuestId = guestId,
        BookingId = bookingId,
        Channel = MessageChannel.Email,
        Direction = MessageDirection.Out,
        Kind = MessageKind.Notification,
        TemplateCode = templateCode,
        Body = string.Empty,
        Status = MessageStatus.Skipped,
        Error = reason,
        DedupKey = dedupKey,
        CreatedAt = now,
        FailedAt = now,
    };

    public void AttachConversation(Guid conversationId) => ConversationId = conversationId;

    /// <summary>Код подтверждения не храним после отправки или ошибки: в истории остаётся только факт.</summary>
    public void ForgetSecrets()
    {
        if (Kind == MessageKind.VerificationCode)
        {
            Parameters = null;
        }
    }

    public void MarkSent(string? externalId, DateTimeOffset now)
    {
        Attempts++;
        Status = MessageStatus.Sent;
        ExternalId = externalId;
        SentAt = now;
        Error = null;
    }

    /// <summary>Временная ошибка: попытка засчитана, повтор — через outbox. True — попытки кончились.</summary>
    public bool RecordTemporaryFailure(string error, DateTimeOffset now)
    {
        Attempts++;
        Error = Trim(error);
        if (Attempts < MaxAttempts)
        {
            return false;
        }

        MarkFailed(error, now);
        return true;
    }

    public void MarkFailed(string error, DateTimeOffset now)
    {
        Status = MessageStatus.Failed;
        Error = Trim(error);
        FailedAt = now;
    }

    /// <summary>
    /// Статус из вебхука. Порядок прихода не гарантирован (документация Meta), поэтому статус только растёт:
    /// «доставлено» после «прочитано» не откатывает. «Ошибка» после доставки не применяется.
    /// </summary>
    public bool ApplyDeliveryStatus(MessageStatus status, DateTimeOffset at, string? error)
    {
        if (Direction != MessageDirection.Out || Status is MessageStatus.Failed or MessageStatus.Skipped)
        {
            return false;
        }

        if (status == MessageStatus.Failed)
        {
            if (Status is MessageStatus.Delivered or MessageStatus.Read)
            {
                return false;
            }

            MarkFailed(error ?? "Сообщение не доставлено.", at);
            return true;
        }

        if (Rank(status) <= Rank(Status))
        {
            return false;
        }

        Status = status;
        SentAt ??= at;

        if (status is MessageStatus.Delivered or MessageStatus.Read)
        {
            DeliveredAt ??= at;
        }

        if (status == MessageStatus.Read)
        {
            ReadAt = at;
        }

        return true;
    }

    /// <summary>Повтор вручную окончательно неотправленного сообщения: снова в очередь.</summary>
    public Result Retry(DateTimeOffset now)
    {
        if (Direction != MessageDirection.Out || Status != MessageStatus.Failed || Recipient is null || Kind == MessageKind.VerificationCode)
        {
            return Hms.SharedKernel.Error.BusinessRule("message.retry_not_allowed", "Повторить можно только неотправленное исходящее сообщение.");
        }

        Status = MessageStatus.Queued;
        Attempts = 0;
        Error = null;
        FailedAt = null;
        CreatedAt = now;
        return Result.Success();
    }

    public void Anonymize()
    {
        Recipient = null;
        Subject = null;
        Body = AnonymizedText;
        Parameters = null;
        ParameterNames = null;
    }

    private static int Rank(MessageStatus s) => s switch
    {
        MessageStatus.Queued => 0,
        MessageStatus.Sent => 1,
        MessageStatus.Delivered => 2,
        MessageStatus.Read => 3,
        _ => -1,
    };

    private static string Trim(string error) => error.Length > 500 ? error[..500] : error;

    /// <summary>Текст WhatsApp — не длиннее 4096 символов (ограничение Cloud API для text.body).</summary>
    private static string Limit(string text) => text.Length <= MaxWhatsAppText ? text : string.Concat(text.AsSpan(0, MaxWhatsAppText - 1), "…");
}

/// <summary>
/// Диалог WhatsApp с номером: окно обслуживания (24 ч после последнего сообщения гостя — правило Meta),
/// передача администратору. Номер — E.164.
/// </summary>
public sealed class Conversation
{
    public static readonly TimeSpan ServiceWindow = TimeSpan.FromHours(24);

    private Conversation()
    {
        Phone = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid HotelId { get; private set; }

    public string Phone { get; private set; }

    public Guid? GuestId { get; private set; }

    /// <summary>Имя из профиля WhatsApp (если гостя нет в базе).</summary>
    public string? ProfileName { get; private set; }

    public DateTimeOffset? LastInboundAt { get; private set; }

    public DateTimeOffset LastMessageAt { get; private set; }

    /// <summary>Гость попросил администратора — автоответчик молчит, пока сотрудник не закроет обращение.</summary>
    public bool NeedsOperator { get; private set; }

    public int UnreadCount { get; private set; }

    public static Conversation Start(Guid hotelId, string phone, Guid? guestId, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        HotelId = hotelId,
        Phone = phone,
        GuestId = guestId,
        LastMessageAt = now,
    };

    public bool IsWindowOpen(DateTimeOffset now) => LastInboundAt is { } last && now < last + ServiceWindow;

    public DateTimeOffset? WindowClosesAt => LastInboundAt + ServiceWindow;

    /// <summary>Входящее сообщение. True — первое обращение (окно было закрыто): автоответчик здоровается.</summary>
    public bool RegisterInbound(Guid? guestId, string? profileName, DateTimeOffset at)
    {
        var first = !IsWindowOpen(at);
        GuestId ??= guestId;
        ProfileName = string.IsNullOrWhiteSpace(profileName) ? ProfileName : profileName.Trim();
        if (LastInboundAt is null || at > LastInboundAt)
        {
            LastInboundAt = at;
        }

        LastMessageAt = at > LastMessageAt ? at : LastMessageAt;
        UnreadCount++;
        return first;
    }

    public void RegisterOutbound(DateTimeOffset at) => LastMessageAt = at > LastMessageAt ? at : LastMessageAt;

    public void RequestOperator() => NeedsOperator = true;

    public void Resolve()
    {
        NeedsOperator = false;
        UnreadCount = 0;
    }

    public void MarkRead() => UnreadCount = 0;

    public void LinkGuest(Guid guestId) => GuestId = guestId;
}
