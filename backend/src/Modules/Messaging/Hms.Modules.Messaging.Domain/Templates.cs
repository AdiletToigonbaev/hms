using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Hms.Modules.Messaging.Contracts;
using Hms.SharedKernel;

namespace Hms.Modules.Messaging.Domain;

/// <summary>Статус шаблона WhatsApp в Meta (модерацию проходит каждый шаблон; до Sprint 12 владелец отмечает вручную).</summary>
public enum WhatsAppTemplateStatus
{
    NotSubmitted,
    Submitted,
    Approved,
    Rejected,
    Paused,
    Disabled,
}

/// <summary>Описание шаблона из каталога: событие, каналы, допустимые переменные и тексты по умолчанию (ru).</summary>
/// <param name="EmailAllowed">Можно ли отправлять письмом (код подтверждения и подтверждение регистрации — только WhatsApp: письмо регистрации уже отправляет Identity).</param>
/// <param name="IsAuthentication">Шаблон категории «Аутентификация» Meta: текст задаёт Meta, код передаётся позиционно в тексте и кнопке.</param>
public sealed record TemplateDefinition(
    string Code,
    string Title,
    NotificationEvent? Event,
    bool EmailAllowed,
    bool IsAuthentication,
    IReadOnlyList<string> Variables,
    string DefaultSubject,
    string DefaultBody,
    string DefaultWhatsAppBody);

/// <summary>
/// Каталог шаблонов. Набор кодов фиксирован (события ТЗ §11 + ручное сообщение + код подтверждения):
/// владелец меняет тексты, но не добавляет новые события.
/// </summary>
public static class TemplateCatalog
{
    public const string DefaultLanguage = "ru";

    public const string RegistrationConfirmed = "registration_confirmed";
    public const string BookingConfirmed = "booking_confirmed";
    public const string PaymentReceived = "payment_received";
    public const string CheckInReminder = "checkin_reminder";
    public const string CheckInDone = "checkin_done";
    public const string CheckOutReminder = "checkout_reminder";
    public const string CheckOutDone = "checkout_done";
    public const string BookingCancelled = "booking_cancelled";
    public const string RefundPaid = "refund_paid";
    public const string ManualNotice = "manual_notice";
    public const string VerificationCode = "verification_code";

    private static readonly string[] Hotel = ["hotel_name", "hotel_contacts", "site_url"];
    private static readonly string[] Booking = ["guest_name", "booking_number", "starts_at", "ends_at", "stay_info", "room_number", "room_type", "total_amount", "booking_url"];

    public static readonly IReadOnlyList<TemplateDefinition> All =
    [
        new(RegistrationConfirmed, "Подтверждение регистрации", NotificationEvent.RegistrationConfirmed, EmailAllowed: false, IsAuthentication: false,
            [.. Hotel, "guest_name"],
            "Регистрация на сайте {{hotel_name}}",
            "Здравствуйте, {{guest_name}}!\n\nВы зарегистрированы на сайте {{hotel_name}}. Забронировать номер можно здесь: {{site_url}}",
            "Здравствуйте, {{guest_name}}! Вы зарегистрированы на сайте {{hotel_name}}. Забронировать номер: {{site_url}}"),
        new(BookingConfirmed, "Бронь подтверждена", NotificationEvent.BookingConfirmed, EmailAllowed: true, IsAuthentication: false,
            [.. Hotel, .. Booking],
            "Бронь {{booking_number}} подтверждена",
            "Здравствуйте, {{guest_name}}!\n\nВаша бронь {{booking_number}} подтверждена.\n{{stay_info}}\nНомер: {{room_number}} ({{room_type}})\nСтоимость: {{total_amount}}\n\nПодробности и отмена — в личном кабинете: {{booking_url}}\n\n{{hotel_name}}\n{{hotel_contacts}}",
            "Здравствуйте, {{guest_name}}! Бронь {{booking_number}} подтверждена. {{stay_info}} Номер {{room_number}}, стоимость {{total_amount}}. Подробности: {{booking_url}}"),
        new(PaymentReceived, "Оплата получена", NotificationEvent.PaymentReceived, EmailAllowed: true, IsAuthentication: false,
            [.. Hotel, .. Booking, "amount"],
            "Оплата по брони {{booking_number}} получена",
            "Здравствуйте, {{guest_name}}!\n\nМы получили оплату {{amount}} по брони {{booking_number}}. Спасибо!\n\n{{hotel_name}}\n{{hotel_contacts}}",
            "Здравствуйте, {{guest_name}}! Получена оплата {{amount}} по брони {{booking_number}}. Спасибо!"),
        new(CheckInReminder, "Напоминание о заезде", NotificationEvent.CheckInReminder, EmailAllowed: true, IsAuthentication: false,
            [.. Hotel, .. Booking],
            "Скоро заезд: бронь {{booking_number}}",
            "Здравствуйте, {{guest_name}}!\n\nНапоминаем о брони {{booking_number}}.\n{{stay_info}}\nНомер: {{room_number}} ({{room_type}})\n\nЖдём вас!\n{{hotel_name}}\n{{hotel_contacts}}",
            "Здравствуйте, {{guest_name}}! Напоминаем о брони {{booking_number}}. {{stay_info}} Номер {{room_number}}. Ждём вас!"),
        new(CheckInDone, "Заселение оформлено", NotificationEvent.CheckInDone, EmailAllowed: true, IsAuthentication: false,
            [.. Hotel, .. Booking],
            "Добро пожаловать в {{hotel_name}}",
            "Здравствуйте, {{guest_name}}!\n\nВы заселены в номер {{room_number}}. Выезд: {{ends_at}}.\nХорошего отдыха!\n\n{{hotel_name}}\n{{hotel_contacts}}",
            "Здравствуйте, {{guest_name}}! Вы заселены в номер {{room_number}}. Выезд: {{ends_at}}. Хорошего отдыха!"),
        new(CheckOutReminder, "Напоминание о выезде", NotificationEvent.CheckOutReminder, EmailAllowed: true, IsAuthentication: false,
            [.. Hotel, .. Booking],
            "Скоро выезд: бронь {{booking_number}}",
            "Здравствуйте, {{guest_name}}!\n\nНапоминаем: выезд по брони {{booking_number}} — {{ends_at}}.\nПродлить можно в личном кабинете ({{booking_url}}) или у администратора.\n\n{{hotel_name}}\n{{hotel_contacts}}",
            "Здравствуйте, {{guest_name}}! Выезд по брони {{booking_number}} — {{ends_at}}. Продлить можно в кабинете или у администратора."),
        new(CheckOutDone, "Выезд оформлен", NotificationEvent.CheckOutDone, EmailAllowed: true, IsAuthentication: false,
            [.. Hotel, .. Booking],
            "Спасибо, что выбрали {{hotel_name}}",
            "Здравствуйте, {{guest_name}}!\n\nВыезд по брони {{booking_number}} оформлен. Спасибо, что выбрали {{hotel_name}}! Будем рады видеть вас снова.\n\n{{site_url}}",
            "Здравствуйте, {{guest_name}}! Выезд по брони {{booking_number}} оформлен. Спасибо, что выбрали {{hotel_name}}!"),
        new(BookingCancelled, "Бронь отменена", NotificationEvent.BookingCancelled, EmailAllowed: true, IsAuthentication: false,
            [.. Hotel, .. Booking, "cancel_info"],
            "Бронь {{booking_number}} отменена",
            "Здравствуйте, {{guest_name}}!\n\nБронь {{booking_number}} отменена.\n{{stay_info}}\n{{cancel_info}}\n\n{{hotel_name}}\n{{hotel_contacts}}",
            "Здравствуйте, {{guest_name}}! Бронь {{booking_number}} отменена. {{cancel_info}}"),
        new(RefundPaid, "Возврат выплачен", NotificationEvent.RefundPaid, EmailAllowed: true, IsAuthentication: false,
            [.. Hotel, .. Booking, "amount"],
            "Возврат по брони {{booking_number}}",
            "Здравствуйте, {{guest_name}}!\n\nВозврат {{amount}} по брони {{booking_number}} выплачен.\n\n{{hotel_name}}\n{{hotel_contacts}}",
            "Здравствуйте, {{guest_name}}! Возврат {{amount}} по брони {{booking_number}} выплачен."),
        new(ManualNotice, "Сообщение от администратора", null, EmailAllowed: true, IsAuthentication: false,
            [.. Hotel, "guest_name", "text"],
            "Сообщение от {{hotel_name}}",
            "Здравствуйте, {{guest_name}}!\n\n{{text}}\n\n{{hotel_name}}\n{{hotel_contacts}}",
            "Здравствуйте, {{guest_name}}! {{text}}"),
        new(VerificationCode, "Код подтверждения", null, EmailAllowed: false, IsAuthentication: true,
            ["code"],
            "Код подтверждения",
            "{{code}} — ваш код подтверждения.",
            "{{code}} — ваш код подтверждения."),
    ];

    private static readonly Dictionary<string, TemplateDefinition> ByCode = All.ToDictionary(d => d.Code, StringComparer.Ordinal);

    public static TemplateDefinition? Find(string code) => ByCode.GetValueOrDefault(code);

    public static TemplateDefinition For(NotificationEvent e) => All.First(d => d.Event == e);
}

/// <summary>Подстановка переменных <c>{{name}}</c>. Имена — латиница в нижнем регистре и «_» (так их принимает Meta в именованных параметрах).</summary>
public static partial class TemplateText
{
    public static IReadOnlyList<string> Placeholders(string text) =>
        Placeholder().Matches(text).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();

    public static string Render(string text, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(text);
        var rendered = Placeholder().Replace(text, m => values.GetValueOrDefault(m.Groups[1].Value) ?? string.Empty);
        // Пустая переменная оставляет пустые строки — схлопываем подряд идущие.
        return BlankLines().Replace(rendered.Trim(), "\n\n");
    }

    /// <summary>Ошибка, если в тексте есть неизвестные переменные или незакрытые скобки.</summary>
    public static Error? Validate(string text, IReadOnlyCollection<string> allowed, string field)
    {
        var unknown = Placeholders(text).Where(p => !allowed.Contains(p)).ToList();
        if (unknown.Count > 0)
        {
            return Error.Validation("template.unknown_variable", $"{field}: неизвестные переменные {string.Join(", ", unknown.Select(u => "{{" + u + "}}"))}.");
        }

        var stripped = Placeholder().Replace(text, string.Empty);
        return stripped.Contains("{{", StringComparison.Ordinal) || stripped.Contains("}}", StringComparison.Ordinal)
            ? Error.Validation("template.bad_placeholder", $"{field}: переменная записывается так: {{{{guest_name}}}}.")
            : null;
    }

    /// <summary>Простой HTML для письма: текст экранируется, абзацы и переносы сохраняются, ссылки кликабельны.</summary>
    public static string ToHtml(string text)
    {
        var sb = new StringBuilder("<div style=\"font-family:Arial,sans-serif;font-size:15px;line-height:1.5;color:#2b2620\">");
        foreach (var paragraph in text.Split("\n\n"))
        {
            var encoded = System.Net.WebUtility.HtmlEncode(paragraph).Replace("\n", "<br>", StringComparison.Ordinal);
            encoded = Url().Replace(encoded, m => $"<a href=\"{m.Value}\">{m.Value}</a>");
            sb.Append("<p>").Append(encoded).Append("</p>");
        }

        return sb.Append("</div>").ToString();
    }

    public static string Money(decimal amount) =>
        string.Create(CultureInfo.GetCultureInfo("ru-RU"), $"{amount:#,##0.##} сом").Replace(' ', ' ');

    [GeneratedRegex(@"\{\{\s*([a-z][a-z0-9_]*)\s*\}\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLines();

    [GeneratedRegex(@"https?://[^\s<]+")]
    private static partial Regex Url();
}

/// <summary>
/// Шаблон гостиницы для кода и языка: тема и текст письма, текст и имя шаблона WhatsApp.
/// Создаёт сидер из каталога; владелец правит тексты и отмечает статус модерации Meta.
/// </summary>
public sealed class MessageTemplate
{
    private MessageTemplate()
    {
        Code = Language = EmailSubject = EmailBody = WhatsAppBody = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid HotelId { get; private set; }

    public string Code { get; private set; }

    public string Language { get; private set; }

    public string EmailSubject { get; private set; }

    public string EmailBody { get; private set; }

    /// <summary>Имя шаблона в WhatsApp Manager (по умолчанию — код).</summary>
    public string? WhatsAppName { get; private set; }

    /// <summary>Текст шаблона, как он подан в Meta (с именованными переменными). Для истории и проверки переменных.</summary>
    public string WhatsAppBody { get; private set; }

    public WhatsAppTemplateStatus WhatsAppStatus { get; private set; }

    /// <summary>Отправлять ли это уведомление вообще (владелец может выключить событие).</summary>
    public bool IsActive { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public uint Version { get; private set; }

    public static MessageTemplate FromDefinition(Guid hotelId, TemplateDefinition d, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(d);
        return new MessageTemplate
        {
            Id = Guid.CreateVersion7(),
            HotelId = hotelId,
            Code = d.Code,
            Language = TemplateCatalog.DefaultLanguage,
            EmailSubject = d.DefaultSubject,
            EmailBody = d.DefaultBody,
            WhatsAppName = d.Code,
            WhatsAppBody = d.DefaultWhatsAppBody,
            WhatsAppStatus = WhatsAppTemplateStatus.NotSubmitted,
            IsActive = true,
            UpdatedAt = now,
        };
    }

    public Result Update(string subject, string body, string whatsAppBody, string? whatsAppName, WhatsAppTemplateStatus status, bool isActive, Guid? by, DateTimeOffset now)
    {
        var definition = TemplateCatalog.Find(Code) ?? throw new InvalidOperationException($"Нет шаблона {Code} в каталоге.");
        var allowed = definition.Variables;
        if (TemplateText.Validate(subject, allowed, "Тема письма") is { } e1)
        {
            return e1;
        }

        if (TemplateText.Validate(body, allowed, "Текст письма") is { } e2)
        {
            return e2;
        }

        if (TemplateText.Validate(whatsAppBody, allowed, "Текст WhatsApp") is { } e3)
        {
            return e3;
        }

        if (definition.Event is null && !isActive)
        {
            return Error.BusinessRule("template.required", "Этот шаблон служебный — его нельзя выключить.");
        }

        EmailSubject = subject.Trim();
        EmailBody = body.Trim();
        WhatsAppBody = whatsAppBody.Trim();
        WhatsAppName = string.IsNullOrWhiteSpace(whatsAppName) ? null : whatsAppName.Trim();
        WhatsAppStatus = status;
        IsActive = isActive;
        UpdatedBy = by;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>Можно ли отправить этот шаблон в WhatsApp: одобрен в Meta и известно имя.</summary>
    public bool WhatsAppReady => WhatsAppStatus == WhatsAppTemplateStatus.Approved && !string.IsNullOrWhiteSpace(WhatsAppName);
}
