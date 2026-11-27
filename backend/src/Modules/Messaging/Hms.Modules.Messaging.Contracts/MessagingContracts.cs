using Hms.SharedKernel;

namespace Hms.Modules.Messaging.Contracts;

/// <summary>
/// События, о которых гостю отправляется уведомление (ТЗ §11, docs/integrations/whatsapp-integration.md §4).
/// Напоминания о заезде и выезде ставит планировщик модуля Messaging, другим модулям их вызывать не нужно.
/// </summary>
public enum NotificationEvent
{
    RegistrationConfirmed,
    BookingConfirmed,
    PaymentReceived,
    CheckInReminder,
    CheckInDone,
    CheckOutReminder,
    CheckOutDone,
    BookingCancelled,
    RefundPaid,
}

/// <summary>
/// Уведомление гостю. Достаточно идентификаторов — подробности (номер брони, время, контакт гостя)
/// модуль Messaging читает сам на момент отправки.
/// </summary>
/// <param name="EntityId">Платёж или возврат — для ключа дедупликации и суммы.</param>
/// <param name="Amount">Сумма оплаты или возврата.</param>
public sealed record GuestNotification(
    NotificationEvent Event,
    Guid? GuestId,
    Guid? BookingId = null,
    Guid? EntityId = null,
    decimal? Amount = null);

/// <summary>
/// Постановка уведомления. Запись добавляется в текущую единицу работы (outbox) и сохраняется вместе
/// с бизнес-изменением вызывающего модуля; отправка — фоном, с повторами. Отдельного SaveChanges нет.
/// </summary>
public interface IGuestNotifications
{
    void Enqueue(GuestNotification notification);
}

/// <summary>Переписка гостя при обезличивании: тексты и адреса стираются, статистика остаётся.</summary>
public interface IGuestMessagesCleanup
{
    /// <summary>Выполняется сразу — вызывайте внутри транзакции вызывающего.</summary>
    Task AnonymizeAsync(Guid guestId, CancellationToken ct);
}

/// <summary>
/// Код подтверждения телефона в WhatsApp (подтверждение регистрации — альтернатива email, решение 2026-11-24).
/// Код создаёт и проверяет модуль Identity; здесь — только доставка.
/// </summary>
public interface IVerificationCodeSender
{
    /// <summary>Можно ли сейчас отправить код (WhatsApp подключён, шаблон кода одобрен).</summary>
    Task<bool> IsAvailableAsync(CancellationToken ct);

    /// <summary>Ставит код в отправку в текущей единице работы. Ошибка — если WhatsApp недоступен.</summary>
    Task<Result> EnqueueAsync(string phoneE164, string code, Guid? guestId, CancellationToken ct);
}

public static class MessagingErrors
{
    public static readonly Error WhatsAppUnavailable =
        Error.BusinessRule("whatsapp.unavailable", "WhatsApp пока не подключён — подтвердите регистрацию по email.");
}
