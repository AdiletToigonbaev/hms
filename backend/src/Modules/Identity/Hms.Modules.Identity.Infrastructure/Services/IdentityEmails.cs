using System.Net;
using Hms.Platform.Email;

namespace Hms.Modules.Identity.Infrastructure.Services;

/// <summary>Тексты писем модуля Identity. Шаблоны уведомлений настраиваются в модуле Messaging (Sprint 10).</summary>
internal static class IdentityEmails
{
    public static EmailMessage ConfirmEmail(string to, string fullName, string link) => Build(
        to,
        "Подтверждение email",
        fullName,
        "Спасибо за регистрацию. Подтвердите адрес электронной почты, чтобы войти в личный кабинет.",
        "Подтвердить email",
        link,
        "Ссылка действует 24 часа. Если вы не регистрировались, просто проигнорируйте это письмо.");

    public static EmailMessage ResetPassword(string to, string fullName, string link) => Build(
        to,
        "Восстановление пароля",
        fullName,
        "Мы получили запрос на смену пароля. Чтобы задать новый пароль, перейдите по ссылке.",
        "Задать новый пароль",
        link,
        "Ссылка действует 2 часа. Если вы не запрашивали смену пароля, проигнорируйте письмо — пароль не изменится.");

    private static EmailMessage Build(string to, string subject, string fullName, string intro, string action, string link, string footer)
    {
        var name = WebUtility.HtmlEncode(fullName);
        var href = WebUtility.HtmlEncode(link);
        // Только содержимое: шапку с названием гостиницы и подвал с контактами добавляет IEmailComposer (Platform).
        var html = $"""
            <p style="margin:0 0 14px">Здравствуйте, {name}!</p>
            <p style="margin:0 0 22px">{WebUtility.HtmlEncode(intro)}</p>
            <p style="margin:0 0 22px">{EmailLayout.Button(link, action)}</p>
            <p style="margin:0 0 10px;font-size:13px;color:#6e625a">Если кнопка не работает, скопируйте ссылку в браузер:<br><a href="{href}" style="{EmailLayout.LinkStyle};word-break:break-all">{href}</a></p>
            <p style="margin:0;font-size:13px;color:#6e625a">{WebUtility.HtmlEncode(footer)}</p>
            """;
        var text = $"""
            Здравствуйте, {fullName}!

            {intro}

            {action}: {link}

            {footer}
            """;
        return new EmailMessage(to, subject, html, text);
    }
}
