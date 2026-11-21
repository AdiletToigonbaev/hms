namespace Hms.Modules.Finance.Contracts;

/// <summary>Способ оплаты (ТЗ §10). MBank и Optima — здесь как ручная регистрация перевода; онлайн-эквайринг — после подключения банков.</summary>
public enum PaymentMethod
{
    Cash,
    BankTransfer,
    CardPos,
    MBank,
    Optima,
}

/// <summary>Платежи для других модулей (карточка гостя, позже — отчёты).</summary>
public interface IPaymentQueries
{
    /// <summary>Чистые поступления по гостям: оплаты − сторно − выплаченные возвраты.</summary>
    Task<IReadOnlyDictionary<Guid, decimal>> PaidByGuestsAsync(IReadOnlyCollection<Guid> guestIds, CancellationToken ct);

    /// <summary>Получено по брони: оплаты − аннулированные − выплаченные возвраты.</summary>
    Task<decimal> ReceivedForBookingAsync(Guid bookingId, CancellationToken ct);
}
