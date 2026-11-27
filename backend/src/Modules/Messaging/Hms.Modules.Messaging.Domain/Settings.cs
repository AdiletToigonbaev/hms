using System.Globalization;
using System.Text;
using Hms.SharedKernel;

namespace Hms.Modules.Messaging.Domain;

/// <summary>
/// Настройки сообщений гостиницы (владелец). Время напоминаний по умолчанию — решение заказчика 2026-11-27
/// (roadmap §6): почасовая — за 60 мин до начала и за 15 мин до конца; посуточная — накануне в 18:00 и за 1 ч до выезда.
/// </summary>
public sealed class MessagingSettings
{
    public const string DefaultGreeting = "Здравствуйте! Это {{hotel_name}}. Напишите цифру нужного раздела:\n{{menu}}";
    public const string DefaultFallback = "Не нашли ответ на ваш вопрос. Напишите цифру нужного раздела:\n{{menu}}";
    public const string DefaultStop = "Вы отписались от сообщений {{hotel_name}} в WhatsApp. Включить их снова можно в личном кабинете на сайте или у администратора.";

    private MessagingSettings()
    {
        GreetingText = DefaultGreeting;
        FallbackText = DefaultFallback;
        StopText = DefaultStop;
    }

    public Guid HotelId { get; private set; }

    public int HourlyStartReminderMinutes { get; private set; } = 60;

    public int HourlyEndReminderMinutes { get; private set; } = 15;

    /// <summary>Время напоминания о посуточном заезде накануне (по часам гостиницы).</summary>
    public TimeOnly NightlyStartReminderTime { get; private set; } = new(18, 0);

    public int NightlyEndReminderMinutes { get; private set; } = 60;

    public bool AutoRepliesEnabled { get; private set; } = true;

    public string GreetingText { get; private set; }

    public string FallbackText { get; private set; }

    public string StopText { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public static MessagingSettings CreateDefault(Guid hotelId, DateTimeOffset now) => new() { HotelId = hotelId, UpdatedAt = now };

    public Result Update(int hourlyStart, int hourlyEnd, TimeOnly nightlyStart, int nightlyEnd, bool autoReplies, string greeting, string fallback, string stop, DateTimeOffset now)
    {
        foreach (var (text, field) in new[] { (greeting, "Приветствие"), (fallback, "Ответ, если вопрос не понят"), (stop, "Ответ на отписку") })
        {
            if (TemplateText.Validate(text, AutoReplyVariables.All, field) is { } error)
            {
                return error;
            }
        }

        HourlyStartReminderMinutes = hourlyStart;
        HourlyEndReminderMinutes = hourlyEnd;
        NightlyStartReminderTime = nightlyStart;
        NightlyEndReminderMinutes = nightlyEnd;
        AutoRepliesEnabled = autoReplies;
        GreetingText = greeting.Trim();
        FallbackText = fallback.Trim();
        StopText = stop.Trim();
        UpdatedAt = now;
        return Result.Success();
    }
}

/// <summary>Когда напоминать о заезде и выезде (чистые функции — проверяются модульными тестами).</summary>
public static class ReminderRules
{
    /// <summary>Момент напоминания о заезде: почасовая — за N минут до начала, посуточная — накануне в заданное время гостиницы.</summary>
    public static DateTimeOffset StartReminderAt(MessagingSettings s, bool hourly, DateTimeOffset startsAt, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (hourly)
        {
            return startsAt.AddMinutes(-s.HourlyStartReminderMinutes);
        }

        var localStart = TimeZoneInfo.ConvertTime(startsAt, zone);
        var dayBefore = DateOnly.FromDateTime(localStart.DateTime).AddDays(-1).ToDateTime(s.NightlyStartReminderTime);
        return new DateTimeOffset(dayBefore, zone.GetUtcOffset(dayBefore));
    }

    public static DateTimeOffset EndReminderAt(MessagingSettings s, bool hourly, DateTimeOffset endsAt)
    {
        ArgumentNullException.ThrowIfNull(s);
        return endsAt.AddMinutes(-(hourly ? s.HourlyEndReminderMinutes : s.NightlyEndReminderMinutes));
    }

    /// <summary>
    /// Пора ли напоминать: момент наступил, событие ещё не началось, а бронь создана до момента напоминания
    /// (только что оформленной брони напоминание не нужно — гость получил подтверждение).
    /// </summary>
    public static bool IsDue(DateTimeOffset remindAt, DateTimeOffset deadline, DateTimeOffset? createdAt, DateTimeOffset now) =>
        remindAt <= now && now < deadline && (createdAt is null || createdAt < remindAt);
}

public enum AutoReplyAction
{
    /// <summary>Ответить текстом.</summary>
    Text,

    /// <summary>Передать диалог администратору.</summary>
    Operator,
}

/// <summary>Пункт меню автоответчика (меню/FAQ без ИИ — решение заказчика 2026-11-24). Тексты правит владелец.</summary>
public sealed class AutoReply
{
    private AutoReply()
    {
        Title = Body = string.Empty;
        Keywords = [];
    }

    public Guid Id { get; private set; }

    public Guid HotelId { get; private set; }

    /// <summary>Цифра в меню (1–9).</summary>
    public int Number { get; private set; }

    public string Title { get; private set; }

    /// <summary>Слова, по которым пункт узнаётся в свободном тексте (начало слова, без регистра).</summary>
    public string[] Keywords { get; private set; }

    public string Body { get; private set; }

    public AutoReplyAction Action { get; private set; }

    public bool IsActive { get; private set; }

    public static AutoReply Create(Guid hotelId, int number, string title, string[] keywords, string body, AutoReplyAction action, bool isActive = true) => new()
    {
        Id = Guid.CreateVersion7(),
        HotelId = hotelId,
        Number = number,
        Title = title.Trim(),
        Keywords = keywords.Select(AutoReplyMatcher.Normalize).Where(k => k.Length > 0).Distinct(StringComparer.Ordinal).ToArray(),
        Body = body.Trim(),
        Action = action,
        IsActive = isActive,
    };

    /// <summary>Пункты меню по умолчанию (ТЗ §11: доступные номера, стоимость, правила, время заезда/выезда, контакты, FAQ).</summary>
    public static IReadOnlyList<AutoReply> Defaults(Guid hotelId) =>
    [
        Create(hotelId, 1, "Номера", ["свобод", "комнат", "мест", "наличи"],
            "Наши номера:\n{{rooms_list}}\n\nСвободные номера на ваши даты и бронирование — на сайте: {{site_url}}", AutoReplyAction.Text),
        Create(hotelId, 2, "Цены", ["цен", "стоим", "стоит", "прайс", "почем", "тариф"],
            "Цены:\n{{price_list}}\n\nТочная стоимость на ваши даты — на сайте: {{site_url}}", AutoReplyAction.Text),
        Create(hotelId, 3, "Заезд и выезд", ["заезд", "выезд", "засел", "высел", "время"],
            "Посуточно: заселение с {{check_in_time}}, выезд до {{check_out_time}}.\nПо часам: от {{hourly_min_hours}} ч, заселение к началу брони.", AutoReplyAction.Text),
        Create(hotelId, 4, "Правила", ["правил", "курен", "животн"],
            "{{rules}}", AutoReplyAction.Text),
        Create(hotelId, 5, "Контакты", ["контакт", "адрес", "телефон", "где", "добрат"],
            "{{hotel_name}}\n{{hotel_contacts}}", AutoReplyAction.Text),
        Create(hotelId, 6, "Частые вопросы", ["вопрос", "faq"],
            "Можно ли отменить бронь? — Да, в личном кабинете на сайте по правилам отмены.\nКак оплатить? — Уточните у администратора.\n\n(Пример: владелец заменяет эти ответы своими в настройках автоответчика.)", AutoReplyAction.Text),
        Create(hotelId, 7, "Связаться с администратором", ["админ", "оператор", "человек", "менеджер", "позов"],
            "Передали ваш вопрос администратору — он ответит здесь в ближайшее время.\n{{hotel_contacts}}", AutoReplyAction.Operator),
    ];
}

/// <summary>Переменные текстов автоответчика.</summary>
public static class AutoReplyVariables
{
    public static readonly IReadOnlyList<string> All =
    [
        "hotel_name", "hotel_contacts", "site_url", "check_in_time", "check_out_time", "hourly_min_hours", "rules", "rooms_list", "price_list", "menu",
    ];
}

/// <summary>Разбор входящего сообщения: отписка, цифра меню или ключевое слово.</summary>
public static class AutoReplyMatcher
{
    private static readonly string[] StopWords = ["стоп", "stop", "отписаться", "отписка", "unsubscribe", "отписать"];
    private static readonly string[] MenuWords = ["меню", "menu", "start", "старт"];

    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text.ToLower(CultureInfo.GetCultureInfo("ru-RU")).Replace('ё', 'е'))
        {
            sb.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public static bool IsStop(string text) => StopWords.Contains(Normalize(text), StringComparer.Ordinal);

    public static bool IsMenu(string text) => MenuWords.Contains(Normalize(text), StringComparer.Ordinal);

    /// <summary>
    /// Пункт по цифре («2») или по ключевым словам: выигрывает пункт с наибольшим числом совпавших слов,
    /// при равенстве — меньший номер («сколько стоит номер?» → «Цены», а не «Номера»).
    /// </summary>
    public static AutoReply? Match(string text, IReadOnlyList<AutoReply> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var normalized = Normalize(text);
        var active = items.Where(i => i.IsActive).OrderBy(i => i.Number).ToList();
        if (int.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            return active.FirstOrDefault(i => i.Number == number);
        }

        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return active
            .Select(i => (Item: i, Hits: words.Count(w => i.Keywords.Any(k => w.StartsWith(k, StringComparison.Ordinal)))))
            .Where(x => x.Hits > 0)
            .OrderByDescending(x => x.Hits).ThenBy(x => x.Item.Number)
            .Select(x => x.Item)
            .FirstOrDefault();
    }

    /// <summary>Меню: «1 — Номера» по строкам.</summary>
    public static string Menu(IReadOnlyList<AutoReply> items) =>
        string.Join('\n', items.Where(i => i.IsActive).OrderBy(i => i.Number).Select(i => $"{i.Number} — {i.Title}"));
}
