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
        var html = $"""
            <!doctype html>
            <html lang="ru"><body style="font-family:Arial,sans-serif;color:#111;line-height:1.5">
            <p>Здравствуйте, {name}!</p>
            <p>{WebUtility.HtmlEncode(intro)}</p>
            <p><a href="{href}" style="display:inline-block;padding:10px 16px;background:#111;color:#fff;text-decoration:none;border-radius:6px">{WebUtility.HtmlEncode(action)}</a></p>
            <p style="font-size:13px;color:#555">Если кнопка не работает, скопируйте ссылку в браузер:<br>{href}</p>
            <p style="font-size:13px;color:#555">{WebUtility.HtmlEncode(footer)}</p>
            </body></html>
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
