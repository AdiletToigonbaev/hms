using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hms.Modules.Hotel.Contracts;
using Microsoft.Extensions.Options;

namespace Hms.Modules.Messaging.Infrastructure;

/// <summary>
/// Секреты WhatsApp Cloud API — только из окружения (<c>Integrations__WhatsApp__AccessToken</c> и т. д.), в БД не хранятся.
/// Несекретные параметры (Phone Number ID, WABA ID) владелец задаёт на экране интеграций.
/// Параметры API сверены с документацией Meta 2026-11-27: Graph API v27.0, POST /{version}/{phone-number-id}/messages.
/// </summary>
public sealed class WhatsAppOptions
{
    public const string Section = "Integrations:WhatsApp";

    /// <summary>Постоянный токен системного пользователя (Bearer).</summary>
    public string? AccessToken { get; set; }

    /// <summary>App Secret — проверка подписи вебхука (X-Hub-Signature-256).</summary>
    public string? AppSecret { get; set; }

    /// <summary>Verify Token — подтверждение адреса вебхука (hub.verify_token).</summary>
    public string? VerifyToken { get; set; }

    public string GraphApiVersion { get; set; } = "v27.0";

    public string BaseUrl { get; set; } = "https://graph.facebook.com";

    public bool SecretsConfigured =>
        !string.IsNullOrWhiteSpace(AccessToken) && !string.IsNullOrWhiteSpace(AppSecret) && !string.IsNullOrWhiteSpace(VerifyToken);
}

/// <summary>Исходящее сообщение WhatsApp: шаблон или свободный текст (только в окне обслуживания).</summary>
/// <param name="Parameters">Параметры шаблона по порядку (имя, значение). Для именованных шаблонов передаётся parameter_name.</param>
/// <param name="IsAuthentication">Шаблон «Аутентификация»: код передаётся позиционно в тексте и в кнопке (index 0).</param>
public sealed record WhatsAppOutgoing(
    string PhoneNumberId,
    string To,
    string? TemplateName,
    string? Language,
    IReadOnlyList<KeyValuePair<string, string>> Parameters,
    bool IsAuthentication,
    string? Text);

/// <param name="Retryable">Временная ошибка (лимиты, сбой сервиса, сеть) — повторить позже.</param>
public sealed record WhatsAppSendResult(bool Success, string? MessageId, int? ErrorCode, string? Error, bool Retryable)
{
    public static WhatsAppSendResult Ok(string id) => new(true, id, null, null, false);
}

/// <summary>Клиент WhatsApp Cloud API. В production — <see cref="CloudApiWhatsAppClient"/>; в тестах подменяется (fake только в тестовых проектах).</summary>
public interface IWhatsAppClient
{
    Task<WhatsAppSendResult> SendAsync(WhatsAppOutgoing message, CancellationToken ct);

    /// <summary>Сведения о номере (проверка токена и Phone Number ID). Null — успех, иначе текст ошибки.</summary>
    Task<string?> CheckPhoneNumberAsync(string phoneNumberId, CancellationToken ct);
}

/// <summary>
/// Классификация кодов ошибок Cloud API (страница Error codes, Meta): повторять — лимиты и временные сбои;
/// не повторять — неверный запрос, токен, шаблон, получатель, окно 24 ч.
/// </summary>
public static class WhatsAppErrorCodes
{
    private static readonly HashSet<int> Retryable =
    [
        1, // запрос не обработан / возможная ошибка сервера
        2, // временная недоступность или перегрузка
        4, // лимит вызовов приложения
        80007, // лимит WABA
        130429, // предел пропускной способности Cloud API
        131000, // неизвестная ошибка — «Try again»
        131016, // сервис временно недоступен
        131056, // слишком много сообщений одному получателю — «Wait and retry»
        133004, // сервер временно недоступен
    ];

    public const int OutsideWindow = 131047;

    public static bool IsRetryable(int code) => Retryable.Contains(code);

    /// <summary>Понятное сотруднику описание частых ошибок (без технических подробностей).</summary>
    public static string Describe(int? code, string? message) => code switch
    {
        OutsideWindow => "Гость не писал больше 24 часов — можно отправить только одобренный шаблон.",
        131026 => "Сообщение не доставлено: у номера нет WhatsApp или устаревшее приложение.",
        131050 => "Гость отказался от сообщений этой компании.",
        130403 => "Гость заблокирован в WhatsApp Business.",
        131048 => "Отправка ограничена Meta: проверьте качество номера в WhatsApp Manager.",
        132000 => "Число параметров не совпадает с шаблоном в Meta.",
        132001 => "Шаблон не найден в Meta на этом языке или не одобрен.",
        132015 => "Шаблон приостановлен Meta из-за низкого качества.",
        132016 => "Шаблон отключён Meta — нужен новый шаблон.",
        0 or 190 => "Токен доступа WhatsApp недействителен или истёк.",
        200 or 3 or 10 => "У токена нет нужных прав в Meta.",
        null => message ?? "Ошибка WhatsApp.",
        _ => $"Ошибка WhatsApp {code}: {message}",
    };
}

/// <summary>Проверка подписи вебхука: <c>X-Hub-Signature-256: sha256=&lt;hex HMAC-SHA256(тело, App Secret)&gt;</c>.</summary>
public static class WhatsAppSignature
{
    public const string Header = "X-Hub-Signature-256";

    public static bool IsValid(byte[] body, string? header, string? appSecret)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (string.IsNullOrEmpty(appSecret) || header is null || !header.StartsWith("sha256=", StringComparison.Ordinal))
        {
            return false;
        }

        byte[] provided;
        try
        {
            provided = Convert.FromHexString(header.AsSpan(7));
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), body);
        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }

    public static string Sign(byte[] body, string appSecret) =>
        "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), body));
}

/// <summary>
/// HTTP-клиент WhatsApp Cloud API по официальной документации Meta (messages reference, template fundamentals,
/// authentication templates). Ответ 200 означает только «принято» — доставка приходит вебхуком.
/// </summary>
internal sealed partial class CloudApiWhatsAppClient(HttpClient http, IOptions<WhatsAppOptions> options) : IWhatsAppClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public async Task<WhatsAppSendResult> SendAsync(WhatsAppOutgoing message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        var o = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{o.BaseUrl.TrimEnd('/')}/{o.GraphApiVersion}/{Uri.EscapeDataString(message.PhoneNumberId)}/messages")
        {
            Content = JsonContent.Create(BuildBody(message), options: Json),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.AccessToken);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            return new WhatsAppSendResult(false, null, null, $"Нет связи с WhatsApp: {ex.Message}", Retryable: true);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new WhatsAppSendResult(false, null, null, "WhatsApp не ответил вовремя.", Retryable: true);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode)
            {
                var id = TryReadMessageId(text);
                return id is null
                    ? new WhatsAppSendResult(false, null, null, "WhatsApp не вернул идентификатор сообщения.", Retryable: true)
                    : WhatsAppSendResult.Ok(id);
            }

            var (code, error) = ReadError(text);
            var retryable = code is { } c ? WhatsAppErrorCodes.IsRetryable(c) : (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests;
            return new WhatsAppSendResult(false, null, code, WhatsAppErrorCodes.Describe(code, error), retryable);
        }
    }

    public async Task<string?> CheckPhoneNumberAsync(string phoneNumberId, CancellationToken ct)
    {
        var o = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{o.BaseUrl.TrimEnd('/')}/{o.GraphApiVersion}/{Uri.EscapeDataString(phoneNumberId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.AccessToken);
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                return null;
            }

            var (code, error) = ReadError(await response.Content.ReadAsStringAsync(ct));
            return WhatsAppErrorCodes.Describe(code, error);
        }
        catch (HttpRequestException ex)
        {
            return $"Нет связи с WhatsApp: {ex.Message}";
        }
    }

    /// <summary>Тело запроса: шаблон (components body, для аутентификации — ещё button url index 0) или текст.</summary>
    internal static object BuildBody(WhatsAppOutgoing m)
    {
        if (m.TemplateName is null)
        {
            return new { messaging_product = "whatsapp", recipient_type = "individual", to = m.To, type = "text", text = new { preview_url = true, body = m.Text } };
        }

        var components = new List<object>();
        if (m.IsAuthentication)
        {
            // Документация: код передаётся дважды — в тексте и в кнопке «Скопировать код» (sub_type url, index "0").
            var code = m.Parameters.Count > 0 ? m.Parameters[0].Value : string.Empty;
            components.Add(new { type = "body", parameters = new[] { new { type = "text", text = code } } });
            components.Add(new { type = "button", sub_type = "url", index = "0", parameters = new[] { new { type = "text", text = code } } });
        }
        else if (m.Parameters.Count > 0)
        {
            // Именованные параметры: шаблон в Meta создаётся с parameter_format = named и теми же именами переменных.
            components.Add(new { type = "body", parameters = m.Parameters.Select(p => new { type = "text", parameter_name = p.Key, text = p.Value }).ToArray() });
        }

        return new
        {
            messaging_product = "whatsapp",
            recipient_type = "individual",
            to = m.To,
            type = "template",
            template = new { name = m.TemplateName, language = new { code = m.Language }, components = components.Count > 0 ? components : null },
        };
    }

    private static string? TryReadMessageId(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array && messages.GetArrayLength() > 0
                && messages[0].TryGetProperty("id", out var id) ? id.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Ошибка Graph API: <c>{"error":{"message","type","code","error_data":{"details"},"fbtrace_id"}}</c>.</summary>
    internal static (int? Code, string? Message) ReadError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("error", out var error))
            {
                return (null, null);
            }

            int? code = error.TryGetProperty("code", out var c) && c.TryGetInt32(out var value) ? value : null;
            var details = error.TryGetProperty("error_data", out var data) && data.TryGetProperty("details", out var d) ? d.GetString() : null;
            var message = error.TryGetProperty("message", out var m) ? m.GetString() : null;
            return (code, details ?? message);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}

/// <summary>
/// Можно ли сейчас отправлять в WhatsApp: ключи на сервере, интеграция включена владельцем, задан Phone Number ID.
/// </summary>
internal sealed class WhatsAppGateway(IHotelProfile hotel, IOptions<WhatsAppOptions> options)
{
    public bool SecretsConfigured => options.Value.SecretsConfigured;

    /// <summary>Phone Number ID, если WhatsApp доступен; иначе null.</summary>
    public async Task<string?> PhoneNumberIdAsync(CancellationToken ct)
    {
        if (!SecretsConfigured)
        {
            return null;
        }

        var settings = await hotel.GetWhatsAppAsync(ct);
        return settings.IsEnabled && !string.IsNullOrWhiteSpace(settings.PhoneNumberId) ? settings.PhoneNumberId : null;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct) => await PhoneNumberIdAsync(ct) is not null;

    public async Task<(bool Available, string Availability, string? BusinessPhone)> StateAsync(CancellationToken ct)
    {
        var settings = await hotel.GetWhatsAppAsync(ct);
        var availability = !SecretsConfigured ? "secrets_missing" : !settings.IsEnabled ? "disabled" : string.IsNullOrWhiteSpace(settings.PhoneNumberId) ? "not_configured" : "active";
        return (availability == "active", availability, settings.BusinessPhone);
    }
}

/// <summary>Проверка соединения для экрана интеграций (модуль Hotel).</summary>
internal sealed class WhatsAppConnectionCheck(IWhatsAppClient client, IOptions<WhatsAppOptions> options) : IWhatsAppConnectionCheck
{
    public bool SecretsConfigured => options.Value.SecretsConfigured;

    public async Task<string?> CheckAsync(string phoneNumberId, CancellationToken ct)
    {
        var problem = await client.CheckPhoneNumberAsync(phoneNumberId, ct);
        return problem is { Length: > 180 } ? problem[..180] : problem;
    }
}
