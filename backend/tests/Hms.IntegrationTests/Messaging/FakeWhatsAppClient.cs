using System.Collections.Concurrent;
using Hms.Modules.Messaging.Infrastructure;

namespace Hms.IntegrationTests.Messaging;

/// <summary>
/// Подмена WhatsApp Cloud API в тестах (fake допустим только в тестовых проектах). Запоминает отправленное,
/// по номеру получателя может вернуть ошибку — временную или окончательную.
/// </summary>
public sealed class FakeWhatsAppClient : IWhatsAppClient
{
    private readonly ConcurrentQueue<WhatsAppOutgoing> _sent = new();
    private readonly ConcurrentDictionary<string, WhatsAppSendResult> _failures = new(StringComparer.Ordinal);

    public IReadOnlyList<WhatsAppOutgoing> Sent => [.. _sent];

    public IReadOnlyList<WhatsAppOutgoing> SentTo(string phone) => [.. _sent.Where(m => m.To == phone)];

    /// <summary>Следующие отправки на этот номер завершатся ошибкой (пока не вызван <see cref="Succeed"/>).</summary>
    public void FailFor(string phone, int code, bool retryable) =>
        _failures[phone] = new WhatsAppSendResult(false, null, code, WhatsAppErrorCodes.Describe(code, "test"), retryable);

    public void Succeed(string phone) => _failures.TryRemove(phone, out _);

    public Task<WhatsAppSendResult> SendAsync(WhatsAppOutgoing message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (_failures.TryGetValue(message.To, out var failure))
        {
            return Task.FromResult(failure);
        }

        _sent.Enqueue(message);
        return Task.FromResult(WhatsAppSendResult.Ok($"wamid.test.{Guid.NewGuid():N}"));
    }

    public Task<string?> CheckPhoneNumberAsync(string phoneNumberId, CancellationToken ct) =>
        Task.FromResult(phoneNumberId == "bad-id" ? "Ошибка WhatsApp 100: test" : null);
}
