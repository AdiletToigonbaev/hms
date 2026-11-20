using Hms.Platform.Outbox;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Hms.Platform.Email;

public sealed record EmailMessage(string To, string Subject, string HtmlBody, string TextBody);

public sealed class EmailOptions
{
    public const string Section = "Email";

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 1025;

    /// <summary>None — без шифрования (Mailpit в разработке); StartTls / SslOnConnect — production.</summary>
    public SecureSocketOptions Security { get; set; } = SecureSocketOptions.None;

    public string? Username { get; set; }

    public string? Password { get; set; }

    public string FromAddress { get; set; } = "no-reply@hms.local";

    public string FromName { get; set; } = "HMS";
}

/// <summary>Немедленная отправка письма. Бизнес-код использует <see cref="IEmailOutbox"/>, а не этот интерфейс.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>Постановка письма в outbox: отправится после фиксации транзакции, с повторами при сбоях.</summary>
public interface IEmailOutbox
{
    void Enqueue(EmailMessage message, string? dedupKey = null);
}

internal sealed class EmailOutbox(IOutbox outbox) : IEmailOutbox
{
    public void Enqueue(EmailMessage message, string? dedupKey = null) =>
        outbox.Enqueue(SendEmailOutboxHandler.MessageType, message, dedupKey);
}

internal sealed class SendEmailOutboxHandler(IEmailSender sender) : OutboxHandler<EmailMessage>
{
    public const string MessageType = "email.send";

    public override string Type => MessageType;

    // Письма содержат одноразовые ссылки (подтверждение email, сброс пароля) — после отправки не храним.
    public override bool IsSensitive => true;

    protected override Task HandleAsync(EmailMessage payload, CancellationToken cancellationToken) =>
        sender.SendAsync(payload, cancellationToken);
}

internal sealed partial class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var settings = options.Value;

        MailboxAddress to;
        try
        {
            to = MailboxAddress.Parse(message.To);
        }
        catch (ParseException ex)
        {
            throw new PermanentOutboxException("Некорректный адрес получателя.", ex);
        }

        using var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        mime.To.Add(to);
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(settings.Host, settings.Port, settings.Security, cancellationToken);
        if (!string.IsNullOrEmpty(settings.Username))
        {
            await client.AuthenticateAsync(settings.Username, settings.Password ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(new SendRequest(mime), cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
        LogSent(logger, message.Subject);
    }

    // Адрес получателя не логируем (ПД).
    [LoggerMessage(Level = LogLevel.Information, Message = "Письмо отправлено: {Subject}")]
    private static partial void LogSent(ILogger logger, string subject);
}
