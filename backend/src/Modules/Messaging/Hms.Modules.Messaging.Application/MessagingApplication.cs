using FluentValidation;
using Hms.Modules.Messaging.Domain;
using Hms.SharedKernel;

namespace Hms.Modules.Messaging.Application;

public sealed record MessageDto(
    Guid Id,
    Guid? GuestId,
    string? GuestName,
    Guid? BookingId,
    string? BookingNumber,
    string Channel,
    string Direction,
    string Kind,
    string? TemplateCode,
    string? TemplateTitle,
    string? Recipient,
    string? Subject,
    string Body,
    string Status,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? ReadAt,
    string? SentByName,
    Guid? FallbackFor,
    bool CanRetry);

public sealed record MessageQuery(
    Guid? GuestId,
    Guid? BookingId,
    Guid? ConversationId,
    string? Channel,
    string? Status,
    string? Kind,
    int? Page,
    int? PageSize);

/// <param name="ConversationId">Ответ в диалоге WhatsApp (номер может быть не связан с карточкой гостя).</param>
/// <param name="Subject">Тема письма; по умолчанию — из шаблона «Сообщение от администратора».</param>
public sealed record SendMessageRequest(Guid? GuestId, Guid? BookingId, Guid? ConversationId, string Channel, string Text, string? Subject);

public sealed record ConversationDto(
    Guid Id,
    string Phone,
    Guid? GuestId,
    string? GuestName,
    string? ProfileName,
    DateTimeOffset LastMessageAt,
    DateTimeOffset? LastInboundAt,
    bool WindowOpen,
    DateTimeOffset? WindowClosesAt,
    bool NeedsOperator,
    int UnreadCount,
    string? LastMessage,
    bool WhatsAppConsent);

public sealed record ConversationQuery(bool? NeedsOperator, string? Search, int? Page, int? PageSize);

/// <summary>Возможности отправки гостю: доступен ли WhatsApp, открыто ли окно — для формы ручной отправки.</summary>
public sealed record SendOptionsDto(
    bool EmailAvailable,
    bool WhatsAppAvailable,
    bool WhatsAppConsent,
    bool WindowOpen,
    DateTimeOffset? WindowClosesAt,
    bool ManualTemplateApproved,
    string? WhatsAppBlockReason);

public sealed record TemplateDto(
    string Code,
    string Language,
    string Title,
    string? Event,
    bool EmailAllowed,
    bool IsAuthentication,
    IReadOnlyList<string> Variables,
    string EmailSubject,
    string EmailBody,
    string? WhatsAppName,
    string WhatsAppBody,
    IReadOnlyList<string> WhatsAppParameters,
    string WhatsAppStatus,
    bool IsActive,
    DateTimeOffset UpdatedAt,
    uint Version);

public sealed record UpdateTemplateRequest(
    string EmailSubject,
    string EmailBody,
    string WhatsAppBody,
    string? WhatsAppName,
    string WhatsAppStatus,
    bool IsActive,
    uint? Version);

/// <summary>Предпросмотр шаблона на примере (данные-заглушки, без реальных гостей).</summary>
public sealed record TemplatePreviewDto(string Subject, string EmailBody, string WhatsAppBody);

public sealed record MessagingSettingsDto(
    int HourlyStartReminderMinutes,
    int HourlyEndReminderMinutes,
    string NightlyStartReminderTime,
    int NightlyEndReminderMinutes,
    bool AutoRepliesEnabled,
    string GreetingText,
    string FallbackText,
    string StopText,
    WhatsAppStateDto WhatsApp,
    uint Version);

/// <param name="Availability">active — подключён и включён; disabled — выключен владельцем; secrets_missing — нет ключей на сервере.</param>
public sealed record WhatsAppStateDto(bool Available, string Availability, bool WebhookConfigured, string? BusinessPhone);

public sealed record UpdateMessagingSettingsRequest(
    int HourlyStartReminderMinutes,
    int HourlyEndReminderMinutes,
    string NightlyStartReminderTime,
    int NightlyEndReminderMinutes,
    bool AutoRepliesEnabled,
    string GreetingText,
    string FallbackText,
    string StopText,
    uint? Version);

public sealed record AutoReplyDto(int Number, string Title, IReadOnlyList<string> Keywords, string Body, string Action, bool IsActive);

public sealed record AutoRepliesDto(IReadOnlyList<AutoReplyDto> Items, IReadOnlyList<string> Variables);

public sealed record SaveAutoRepliesRequest(IReadOnlyList<AutoReplyDto> Items);

/// <summary>Ответ автоответчика на пробное сообщение (проверка меню без WhatsApp).</summary>
public sealed record AutoReplyTestRequest(string Text, bool FirstContact);

public sealed record AutoReplyTestDto(string Reply, string Outcome);

public interface IMessageService
{
    Task<PagedResult<MessageDto>> ListAsync(MessageQuery q, CancellationToken ct);

    Task<Result<SendOptionsDto>> SendOptionsAsync(Guid? guestId, Guid? conversationId, CancellationToken ct);

    Task<Result<MessageDto>> SendAsync(SendMessageRequest r, CancellationToken ct);

    Task<Result<MessageDto>> RetryAsync(Guid id, CancellationToken ct);
}

public interface IConversationService
{
    Task<PagedResult<ConversationDto>> ListAsync(ConversationQuery q, CancellationToken ct);

    Task<Result<ConversationDto>> GetAsync(Guid id, CancellationToken ct);

    /// <summary>Сотрудник открыл диалог: непрочитанные сброшены.</summary>
    Task<Result<ConversationDto>> MarkReadAsync(Guid id, CancellationToken ct);

    /// <summary>Обращение к администратору закрыто — автоответчик снова отвечает.</summary>
    Task<Result<ConversationDto>> ResolveAsync(Guid id, CancellationToken ct);

    Task<int> NeedsOperatorCountAsync(CancellationToken ct);
}

public interface ITemplateService
{
    Task<IReadOnlyList<TemplateDto>> ListAsync(CancellationToken ct);

    Task<Result<TemplateDto>> UpdateAsync(string code, string language, UpdateTemplateRequest r, CancellationToken ct);

    Task<Result<TemplatePreviewDto>> PreviewAsync(string code, string language, CancellationToken ct);
}

public interface IMessagingSettingsService
{
    Task<MessagingSettingsDto> GetAsync(CancellationToken ct);

    Task<Result<MessagingSettingsDto>> UpdateAsync(UpdateMessagingSettingsRequest r, CancellationToken ct);

    Task<AutoRepliesDto> GetAutoRepliesAsync(CancellationToken ct);

    Task<Result<AutoRepliesDto>> SaveAutoRepliesAsync(SaveAutoRepliesRequest r, CancellationToken ct);

    Task<AutoReplyTestDto> TestAutoReplyAsync(AutoReplyTestRequest r, CancellationToken ct);
}

public static class MessagingAppErrors
{
    public static readonly Error NotFound = Error.NotFound("message.not_found", "Сообщение не найдено.");
    public static readonly Error ConversationNotFound = Error.NotFound("conversation.not_found", "Диалог не найден.");
    public static readonly Error TemplateNotFound = Error.NotFound("template.not_found", "Шаблон не найден.");
    public static readonly Error GuestNotFound = Error.NotFound("guest.not_found", "Гость не найден.");
    public static readonly Error RecipientRequired = Error.Validation("message.recipient_required", "Выберите гостя или диалог.");
    public static readonly Error NoEmail = Error.BusinessRule("message.no_email", "У гостя не указан email.");
    public static readonly Error NoPhone = Error.BusinessRule("message.no_phone", "У гостя не указан телефон WhatsApp.");
    public static readonly Error NoConsent = Error.BusinessRule("whatsapp.no_consent", "Гость не давал согласия на сообщения в WhatsApp.");
    public static readonly Error OutsideWindow = Error.BusinessRule(
        "whatsapp.outside_window",
        "Гость не писал больше 24 часов — по правилам WhatsApp можно отправить только одобренный шаблон «Сообщение от администратора». Он ещё не одобрен в Meta.");

    public static readonly Error ConcurrencyConflict = Error.Conflict("concurrency.conflict", "Данные изменил другой пользователь — обновите страницу.");
}

public sealed class SendMessageRequestValidator : AbstractValidator<SendMessageRequest>
{
    public SendMessageRequestValidator()
    {
        RuleFor(x => x.Channel).Must(c => Enum.TryParse<MessageChannel>(c, out _)).WithMessage("Выберите канал: Email или WhatsApp.");
        RuleFor(x => x.Text).NotEmpty().WithMessage("Введите текст сообщения.").MaximumLength(2000).WithMessage("Не больше 2000 символов.");
        RuleFor(x => x.Subject).MaximumLength(150).WithMessage("Тема — не больше 150 символов.");
        RuleFor(x => x).Must(x => x.GuestId is not null || x.ConversationId is not null).WithMessage("Выберите гостя или диалог.");
    }
}

public sealed class UpdateTemplateRequestValidator : AbstractValidator<UpdateTemplateRequest>
{
    public UpdateTemplateRequestValidator()
    {
        RuleFor(x => x.EmailSubject).NotEmpty().WithMessage("Введите тему письма.").MaximumLength(150).WithMessage("Тема — не больше 150 символов.");
        RuleFor(x => x.EmailBody).NotEmpty().WithMessage("Введите текст письма.").MaximumLength(4000).WithMessage("Текст письма — не больше 4000 символов.");
        // Ограничение Meta на текст шаблона — 1024 символа.
        RuleFor(x => x.WhatsAppBody).NotEmpty().WithMessage("Введите текст WhatsApp.").MaximumLength(1024).WithMessage("Текст WhatsApp — не больше 1024 символов (ограничение Meta).");
        RuleFor(x => x.WhatsAppName).MaximumLength(512).Matches("^[a-z0-9_]+$").When(x => !string.IsNullOrWhiteSpace(x.WhatsAppName))
            .WithMessage("Имя шаблона в Meta — латиница в нижнем регистре, цифры и «_».");
        RuleFor(x => x.WhatsAppStatus).Must(s => Enum.TryParse<WhatsAppTemplateStatus>(s, out _)).WithMessage("Неизвестный статус шаблона.");
    }
}

public sealed class UpdateMessagingSettingsRequestValidator : AbstractValidator<UpdateMessagingSettingsRequest>
{
    public UpdateMessagingSettingsRequestValidator()
    {
        RuleFor(x => x.HourlyStartReminderMinutes).InclusiveBetween(10, 24 * 60).WithMessage("Напоминание о почасовой брони — за 10 минут … 24 часа.");
        RuleFor(x => x.HourlyEndReminderMinutes).InclusiveBetween(5, 180).WithMessage("Напоминание о конце почасовой брони — за 5 … 180 минут.");
        RuleFor(x => x.NightlyEndReminderMinutes).InclusiveBetween(15, 12 * 60).WithMessage("Напоминание о выезде — за 15 минут … 12 часов.");
        RuleFor(x => x.NightlyStartReminderTime).Must(t => TimeOnly.TryParse(t, System.Globalization.CultureInfo.InvariantCulture, out _))
            .WithMessage("Время напоминания накануне — в формате ЧЧ:ММ.");
        RuleFor(x => x.GreetingText).NotEmpty().MaximumLength(1000).WithMessage("Приветствие — от 1 до 1000 символов.");
        RuleFor(x => x.FallbackText).NotEmpty().MaximumLength(1000).WithMessage("Ответ, если вопрос не понят, — от 1 до 1000 символов.");
        RuleFor(x => x.StopText).NotEmpty().MaximumLength(1000).WithMessage("Ответ на отписку — от 1 до 1000 символов.");
    }
}

public sealed class SaveAutoRepliesRequestValidator : AbstractValidator<SaveAutoRepliesRequest>
{
    public SaveAutoRepliesRequestValidator()
    {
        RuleFor(x => x.Items).NotNull().Must(i => i.Count is >= 1 and <= 9).WithMessage("В меню — от 1 до 9 пунктов.");
        RuleFor(x => x.Items).Must(i => i.Select(x => x.Number).Distinct().Count() == i.Count).When(x => x.Items is not null)
            .WithMessage("Цифры пунктов меню не должны повторяться.");
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.Number).InclusiveBetween(1, 9).WithMessage("Цифра пункта — от 1 до 9.");
            item.RuleFor(i => i.Title).NotEmpty().MaximumLength(40).WithMessage("Название пункта — от 1 до 40 символов.");
            item.RuleFor(i => i.Body).NotEmpty().MaximumLength(1500).WithMessage("Ответ — от 1 до 1500 символов.");
            item.RuleFor(i => i.Keywords).Must(k => k is null || (k.Count <= 20 && k.All(w => w.Length <= 30))).WithMessage("До 20 ключевых слов по 30 символов.");
            item.RuleFor(i => i.Action).Must(a => Enum.TryParse<AutoReplyAction>(a, out _)).WithMessage("Неизвестное действие пункта.");
        });
    }
}

public sealed class AutoReplyTestRequestValidator : AbstractValidator<AutoReplyTestRequest>
{
    public AutoReplyTestRequestValidator()
    {
        RuleFor(x => x.Text).NotEmpty().MaximumLength(500).WithMessage("Введите сообщение (до 500 символов).");
    }
}
