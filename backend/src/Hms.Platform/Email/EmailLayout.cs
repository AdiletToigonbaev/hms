using System.Net;
using Microsoft.Extensions.Options;

namespace Hms.Platform.Email;

/// <summary>Сведения о гостинице для оформления писем (шапка, подвал, имя отправителя).</summary>
public sealed record EmailBrand(string HotelName, string? Address, string? Phone, string? WebsiteUrl);

/// <summary>
/// Источник сведений о гостинице для писем. Реализация — модуль Hotel (из настроек гостиницы);
/// по умолчанию — нейтральное «Гостиница» (Platform не зависит от модулей).
/// </summary>
public interface IEmailBrandProvider
{
    Task<EmailBrand> GetAsync(CancellationToken ct);
}

internal sealed class DefaultEmailBrandProvider : IEmailBrandProvider
{
    public Task<EmailBrand> GetAsync(CancellationToken ct) => Task.FromResult(new EmailBrand("Гостиница", null, null, null));
}

/// <summary>
/// Оформляет письмо перед отправкой: модули передают только содержимое (фрагмент HTML и текст),
/// шапка с названием, подвал с контактами и имя отправителя — здесь, единые для всех писем.
/// </summary>
public interface IEmailComposer
{
    Task<EmailMessage> ComposeAsync(EmailMessage content, CancellationToken ct);
}

internal sealed class EmailComposer(IEmailBrandProvider brands, IOptions<EmailOptions> options) : IEmailComposer
{
    public async Task<EmailMessage> ComposeAsync(EmailMessage content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        var brand = await brands.GetAsync(ct);
        // Имя отправителя — из настроек почты, а если не задано — название гостиницы («Great Hotel <no-reply@…>»).
        var fromName = string.IsNullOrWhiteSpace(options.Value.FromName) ? brand.HotelName : options.Value.FromName;
        return content with
        {
            HtmlBody = EmailLayout.Wrap(brand, content.Subject, content.HtmlBody),
            TextBody = EmailLayout.WrapText(brand, content.TextBody),
            FromName = fromName,
        };
    }
}

/// <summary>
/// Макет письма в стиле сайта (docs/design/design-direction.md): песочный фон, белая карточка,
/// терракотовая кнопка. Таблицы и inline-стили — так письмо одинаково выглядит в Gmail, Outlook, Mail.ru.
/// Lora в почтовых программах нет — заголовок Georgia (тоже с засечками).
/// </summary>
public static class EmailLayout
{
    private const string Sand = "#f3ece2";
    private const string Primary = "#b5562f";
    private const string OnPrimary = "#fffaf5";
    private const string Text = "#2b2420";
    private const string Muted = "#6e625a";
    private const string Border = "#e6dccf";
    private const string SansFont = "Arial,Helvetica,sans-serif";
    private const string SerifFont = "Georgia,'Times New Roman',serif";

    /// <summary>Кнопка-ссылка в фирменном цвете.</summary>
    public static string Button(string href, string label) =>
        $"<a href=\"{WebUtility.HtmlEncode(href)}\" style=\"display:inline-block;padding:12px 22px;background:{Primary};" +
        $"color:{OnPrimary};text-decoration:none;border-radius:8px;font-weight:bold;font-family:{SansFont}\">{WebUtility.HtmlEncode(label)}</a>";

    /// <summary>Стиль ссылок внутри текста писем.</summary>
    public static string LinkStyle => $"color:{Primary}";

    public static string Wrap(EmailBrand brand, string subject, string contentHtml)
    {
        ArgumentNullException.ThrowIfNull(brand);
        var name = WebUtility.HtmlEncode(brand.HotelName);
        var letter = WebUtility.HtmlEncode(Monogram(brand.HotelName));
        return $"""
            <!doctype html>
            <html lang="ru">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{WebUtility.HtmlEncode(subject)}</title></head>
            <body style="margin:0;padding:0;background:{Sand}">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:{Sand}">
            <tr><td align="center" style="padding:28px 12px">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px">
            <tr><td style="padding:0 4px 18px;font-family:{SerifFont};font-size:22px;color:{Text}">
            <span style="display:inline-block;width:36px;height:36px;line-height:36px;text-align:center;background:{Primary};color:{OnPrimary};border-radius:10px;font-size:20px;vertical-align:middle">{letter}</span>
            <span style="vertical-align:middle;padding-left:10px">{name}</span>
            </td></tr>
            <tr><td style="background:#ffffff;border:1px solid {Border};border-radius:12px;padding:28px;font-family:{SansFont};font-size:15px;line-height:1.55;color:{Text}">
            {contentHtml}
            </td></tr>
            <tr><td style="padding:18px 4px;font-family:{SansFont};font-size:12px;line-height:1.6;color:{Muted}">
            {Footer(brand)}
            </td></tr>
            </table>
            </td></tr>
            </table>
            </body>
            </html>
            """;
    }

    /// <summary>Текстовая версия: тот же подвал с контактами.</summary>
    public static string WrapText(EmailBrand brand, string text)
    {
        ArgumentNullException.ThrowIfNull(brand);
        var contacts = new[] { brand.HotelName, brand.Address, brand.Phone, brand.WebsiteUrl }
            .Where(s => !string.IsNullOrWhiteSpace(s));
        return $"{text.TrimEnd()}\n\n—\n{string.Join("\n", contacts)}\n";
    }

    private static string Footer(EmailBrand brand)
    {
        var lines = new List<string> { WebUtility.HtmlEncode(brand.HotelName) };
        if (!string.IsNullOrWhiteSpace(brand.Address))
        {
            lines.Add(WebUtility.HtmlEncode(brand.Address));
        }

        var contacts = new List<string>();
        if (!string.IsNullOrWhiteSpace(brand.Phone))
        {
            contacts.Add(WebUtility.HtmlEncode(brand.Phone));
        }

        if (Uri.TryCreate(brand.WebsiteUrl, UriKind.Absolute, out var site) && (site.Scheme == Uri.UriSchemeHttps || site.Scheme == Uri.UriSchemeHttp))
        {
            contacts.Add($"<a href=\"{WebUtility.HtmlEncode(site.ToString())}\" style=\"color:{Muted}\">{WebUtility.HtmlEncode(site.Host)}</a>");
        }

        if (contacts.Count > 0)
        {
            lines.Add(string.Join(" · ", contacts));
        }

        return string.Join("<br>", lines);
    }

    // Та же буква, что в монограмме сайта (frontend: brandLetter).
    private static string Monogram(string name)
    {
        var trimmed = name.Trim();
        return trimmed.Length == 0 ? "Г" : trimmed[..1].ToUpperInvariant();
    }
}
