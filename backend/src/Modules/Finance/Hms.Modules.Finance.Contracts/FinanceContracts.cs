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

/// <summary>Платёж для отчётов. Аннулирование — отдельная запись с отрицательной суммой (Type = Reversal) на дату аннулирования.</summary>
public sealed record PaymentFact(Guid Id, Guid? BookingId, string Type, PaymentMethod Method, decimal Amount, string Status, DateTimeOffset PaidAt, Guid? RegisteredBy);

/// <summary>Возврат для отчётов: запрошен, решён, выплачен (Status: Requested | Approved | Rejected | Paid).</summary>
public sealed record RefundFact(
    Guid Id, Guid PaymentId, Guid? BookingId, decimal Amount, PaymentMethod Method, string Status, string Reason,
    DateTimeOffset RequestedAt, Guid? RequestedBy, DateTimeOffset? DecidedAt, Guid? DecidedBy, DateTimeOffset? PaidAt, Guid? PaidBy);

/// <summary>Расход для отчётов (аннулированные не включаются).</summary>
public sealed record ExpenseFact(Guid Id, DateOnly Date, string CategoryName, decimal Amount, PaymentMethod Method, string? Supplier, string Description, Guid? CreatedBy);

/// <summary>Финансовые факты для отчётов и дашбордов (только чтение).</summary>
public interface IFinanceFacts
{
    /// <summary>Платежи (включая аннулирования) с датой оплаты в [from, to).</summary>
    Task<IReadOnlyList<PaymentFact>> PaymentsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    /// <summary>Возвраты, запрошенные, решённые или выплаченные в [from, to).</summary>
    Task<IReadOnlyList<RefundFact>> RefundsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    /// <summary>Расходы с датой в [from, to] (даты гостиницы), без аннулированных.</summary>
    Task<IReadOnlyList<ExpenseFact>> ExpensesAsync(DateOnly from, DateOnly to, CancellationToken ct);

    /// <summary>Чистые поступления по броням за всё время: оплаты − аннулирования − выплаченные возвраты (для долгов).</summary>
    Task<IReadOnlyDictionary<Guid, decimal>> NetReceivedAsync(IReadOnlyCollection<Guid> bookingIds, CancellationToken ct);
}
