using Hms.Modules.Finance.Contracts;
using Hms.SharedKernel;

namespace Hms.Modules.Finance.Domain;

/// <summary>За что платёж (ТЗ §10). Reversal — сторно ошибочной регистрации (отрицательная сумма).</summary>
public enum PaymentType
{
    Prepayment,
    Stay,
    Service,
    Penalty,
    Other,
    Reversal,
}

/// <summary>Manual — сотрудник зарегистрировал полученные деньги; Online — эквайринг банка (после подключения).</summary>
public enum PaymentChannel
{
    Manual,
    Online,
}

/// <summary>
/// Статусы платежа (docs/database/database-design.md §4). Ручной платёж создаётся сразу в Paid.
/// Статусы возврата выводятся из возвратов; Reversed — платёж отменён сторно.
/// </summary>
public enum PaymentStatus
{
    Pending,
    Processing,
    Paid,
    Failed,
    Cancelled,
    RefundPending,
    PartiallyRefunded,
    Refunded,
    Reversed,
}

public enum PaymentTransactionKind
{
    Charge,
    Refund,
    Reversal,
    StatusChange,
}

public enum RefundStatus
{
    Requested,
    Approved,
    Rejected,
    Paid,
}

public static class FinanceErrors
{
    public static readonly Error AmountInvalid = Error.Validation("payment.amount_invalid", "Сумма должна быть больше нуля.");
    public static readonly Error NotFound = Error.NotFound("payment.not_found", "Платёж не найден.");
    public static readonly Error AlreadyReversed = Error.Conflict("payment.already_reversed", "Платёж уже аннулирован.");
    public static readonly Error ReversalNotAllowed = Error.BusinessRule("payment.reversal_not_allowed", "Аннулировать можно только ручной платёж без возвратов.");
    public static readonly Error ReasonRequired = Error.Validation("payment.reason_required", "Укажите причину.");
    public static readonly Error NotRefundable = Error.BusinessRule("refund.not_refundable", "По этому платежу возврат невозможен.");
    public static readonly Error RefundNotFound = Error.NotFound("refund.not_found", "Возврат не найден.");
    public static readonly Error ExpenseNotFound = Error.NotFound("expense.not_found", "Расход не найден.");
    public static readonly Error ExpenseVoided = Error.Conflict("expense.voided", "Расход аннулирован — изменить его нельзя.");
    public static readonly Error CategoryNotFound = Error.NotFound("expense.category_not_found", "Категория расходов не найдена.");
    public static readonly Error CategoryInactive = Error.BusinessRule("expense.category_inactive", "Категория отключена — выберите другую.");

    public static Error RefundTooLarge(decimal available) =>
        Error.BusinessRule("refund.amount_exceeds_payment", $"Вернуть можно не больше {available:0.##} сом по этому платежу.");

    public static Error RefundInvalidTransition(RefundStatus from, string action) =>
        Error.Conflict("refund.invalid_transition", $"Нельзя {action}: возврат в статусе «{from}».");
}

/// <summary>
/// Платёж. Суммы, способ, дата и автор неизменяемы; меняются только статус и выплаченная сумма возвратов,
/// каждое изменение — строка в журнале операций (<see cref="PaymentTransaction"/>). Ошибку исправляют сторно.
/// </summary>
public sealed class Payment
{
    private readonly List<PaymentTransaction> _transactions = [];

    private Payment()
    {
    }

    public Guid Id { get; private set; }

    public Guid HotelId { get; private set; }

    public Guid? BookingId { get; private set; }

    public Guid? GuestId { get; private set; }

    public PaymentType Type { get; private set; }

    public PaymentMethod Method { get; private set; }

    public PaymentChannel Channel { get; private set; }

    /// <summary>Сумма; у сторно — отрицательная.</summary>
    public decimal Amount { get; private set; }

    public PaymentStatus Status { get; private set; }

    /// <summary>Для сторно — какой платёж отменён.</summary>
    public Guid? ReversalOfPaymentId { get; private set; }

    /// <summary>Сколько уже выплачено возвратами (выводится из возвратов).</summary>
    public decimal RefundedAmount { get; private set; }

    public DateTimeOffset PaidAt { get; private set; }

    public Guid? RegisteredBy { get; private set; }

    public string? Comment { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public uint Version { get; private set; }

    public IReadOnlyList<PaymentTransaction> Transactions => _transactions;

    /// <summary>Платёж учитывается в поступлениях (получен и не отменён целиком).</summary>
    public bool IsReceived => Status is PaymentStatus.Paid or PaymentStatus.RefundPending or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded or PaymentStatus.Reversed;

    /// <summary>Чистая сумма для баланса брони: получено − выплаченные возвраты (сторно — отдельной отрицательной записью).</summary>
    public decimal NetAmount => IsReceived ? Amount - RefundedAmount : 0m;

    /// <summary>Ручная регистрация полученных денег: платёж сразу «Оплачен».</summary>
    public static Result<Payment> RegisterManual(
        Guid hotelId, Guid? bookingId, Guid? guestId, PaymentType type, PaymentMethod method, decimal amount, Guid? by, string? comment, DateTimeOffset now)
    {
        if (amount <= 0 || decimal.Round(amount, 2) != amount)
        {
            return FinanceErrors.AmountInvalid;
        }

        if (type == PaymentType.Reversal)
        {
            return Error.Validation("payment.type_invalid", "Аннулирование создаётся только из платежа.");
        }

        var payment = new Payment
        {
            Id = Guid.CreateVersion7(),
            HotelId = hotelId,
            BookingId = bookingId,
            GuestId = guestId,
            Type = type,
            Method = method,
            Channel = PaymentChannel.Manual,
            Amount = amount,
            Status = PaymentStatus.Paid,
            PaidAt = now,
            RegisteredBy = by,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
            CreatedAt = now,
        };
        payment._transactions.Add(new PaymentTransaction(payment.Id, PaymentTransactionKind.Charge, amount, PaymentStatus.Paid, by, null, now));
        return payment;
    }

    /// <summary>
    /// Сторно ошибочной регистрации: исходный платёж — «Сторнирован», создаётся запись с обратной суммой.
    /// Нельзя, если по платежу есть возвраты (кроме отклонённых): сначала возвраты.
    /// </summary>
    public Result<Payment> Reverse(string reason, bool hasActiveRefunds, Guid? by, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return FinanceErrors.ReasonRequired;
        }

        if (Status == PaymentStatus.Reversed)
        {
            return FinanceErrors.AlreadyReversed;
        }

        if (Type == PaymentType.Reversal || Channel != PaymentChannel.Manual || Status != PaymentStatus.Paid || hasActiveRefunds)
        {
            return FinanceErrors.ReversalNotAllowed;
        }

        var text = reason.Trim();
        var reversal = new Payment
        {
            Id = Guid.CreateVersion7(),
            HotelId = HotelId,
            BookingId = BookingId,
            GuestId = GuestId,
            Type = PaymentType.Reversal,
            Method = Method,
            Channel = PaymentChannel.Manual,
            Amount = -Amount,
            Status = PaymentStatus.Paid,
            ReversalOfPaymentId = Id,
            PaidAt = now,
            RegisteredBy = by,
            Comment = text,
            CreatedAt = now,
        };
        reversal._transactions.Add(new PaymentTransaction(reversal.Id, PaymentTransactionKind.Reversal, -Amount, PaymentStatus.Paid, by, text, now));
        Status = PaymentStatus.Reversed;
        _transactions.Add(new PaymentTransaction(Id, PaymentTransactionKind.Reversal, -Amount, PaymentStatus.Reversed, by, text, now));
        return reversal;
    }

    /// <summary>Можно ли оформить возврат по платежу.</summary>
    public bool IsRefundable => Type != PaymentType.Reversal && Status is PaymentStatus.Paid or PaymentStatus.RefundPending or PaymentStatus.PartiallyRefunded;

    /// <summary>
    /// Пересчёт статуса по возвратам (источник истины — возвраты): есть одобренный, но не выплаченный — «Ожидает возврата»;
    /// выплачено всё — «Возвращён»; часть — «Частично возвращён».
    /// </summary>
    public void ApplyRefunds(decimal approvedNotPaid, decimal paid, Guid? by, DateTimeOffset now)
    {
        if (Status is PaymentStatus.Reversed || !IsReceived)
        {
            return;
        }

        var status = approvedNotPaid > 0 ? PaymentStatus.RefundPending
            : paid <= 0 ? PaymentStatus.Paid
            : paid >= Amount ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
        var refundedDelta = paid - RefundedAmount;
        if (status == Status && refundedDelta == 0)
        {
            return;
        }

        RefundedAmount = paid;
        Status = status;
        _transactions.Add(new PaymentTransaction(Id, refundedDelta > 0 ? PaymentTransactionKind.Refund : PaymentTransactionKind.StatusChange,
            refundedDelta > 0 ? -refundedDelta : 0m, status, by, null, now));
    }
}

/// <summary>Журнал операций по платежу — только добавление.</summary>
public sealed class PaymentTransaction
{
    private PaymentTransaction()
    {
    }

    public PaymentTransaction(Guid paymentId, PaymentTransactionKind kind, decimal amount, PaymentStatus status, Guid? by, string? comment, DateTimeOffset at)
    {
        Id = Guid.CreateVersion7();
        PaymentId = paymentId;
        Kind = kind;
        Amount = amount;
        Status = status;
        CreatedBy = by;
        Comment = comment;
        OccurredAt = at;
    }

    public Guid Id { get; private set; }

    public Guid PaymentId { get; private set; }

    public PaymentTransactionKind Kind { get; private set; }

    public decimal Amount { get; private set; }

    public PaymentStatus Status { get; private set; }

    /// <summary>Провайдер и его номер операции — для онлайн-оплаты (после подключения банков).</summary>
    public string? Provider { get; private set; }

    public string? ProviderTxnId { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public string? Comment { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
}

/// <summary>
/// Возврат денег гостю (ТЗ §10): запрос → решение (в пределах лимита утверждающего) → выплата.
/// Сумма всех не отклонённых возвратов по платежу не больше суммы платежа.
/// </summary>
public sealed class Refund
{
    private Refund()
    {
        Reason = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid HotelId { get; private set; }

    public Guid PaymentId { get; private set; }

    public Guid? BookingId { get; private set; }

    public decimal Amount { get; private set; }

    public string Reason { get; private set; }

    /// <summary>Как вернуть деньги (может отличаться от способа оплаты).</summary>
    public PaymentMethod Method { get; private set; }

    public RefundStatus Status { get; private set; }

    public Guid? RequestedBy { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    public Guid? DecidedBy { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    public string? DecisionComment { get; private set; }

    public Guid? PaidBy { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    public uint Version { get; private set; }

    /// <summary>Учитывается в лимите по платежу (не отклонён).</summary>
    public bool IsActive => Status != RefundStatus.Rejected;

    /// <param name="reserved">Сумма уже оформленных (не отклонённых) возвратов по платежу.</param>
    public static Result<Refund> Request(Payment payment, decimal amount, string reason, PaymentMethod method, decimal reserved, Guid? by, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(payment);
        if (amount <= 0 || decimal.Round(amount, 2) != amount)
        {
            return FinanceErrors.AmountInvalid;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return FinanceErrors.ReasonRequired;
        }

        if (!payment.IsRefundable)
        {
            return FinanceErrors.NotRefundable;
        }

        var available = payment.Amount - reserved;
        if (amount > available)
        {
            return FinanceErrors.RefundTooLarge(Math.Max(0, available));
        }

        return new Refund
        {
            Id = Guid.CreateVersion7(),
            HotelId = payment.HotelId,
            PaymentId = payment.Id,
            BookingId = payment.BookingId,
            Amount = amount,
            Reason = reason.Trim(),
            Method = method,
            Status = RefundStatus.Requested,
            RequestedBy = by,
            RequestedAt = now,
        };
    }

    /// <param name="limit">Лимит утверждающего (null — без лимита).</param>
    public Result Approve(decimal? limit, Guid? by, string? comment, DateTimeOffset now)
    {
        if (Status != RefundStatus.Requested)
        {
            return FinanceErrors.RefundInvalidTransition(Status, "утвердить");
        }

        if (limit is { } max && Amount > max)
        {
            return Error.Forbidden("refund.limit_exceeded", $"Сумма больше вашего лимита на утверждение возвратов ({max:0.##} сом). Нужен владелец.");
        }

        Status = RefundStatus.Approved;
        Decide(by, comment, now);
        return Result.Success();
    }

    public Result Reject(string comment, Guid? by, DateTimeOffset now)
    {
        if (Status != RefundStatus.Requested)
        {
            return FinanceErrors.RefundInvalidTransition(Status, "отклонить");
        }

        if (string.IsNullOrWhiteSpace(comment))
        {
            return FinanceErrors.ReasonRequired;
        }

        Status = RefundStatus.Rejected;
        Decide(by, comment, now);
        return Result.Success();
    }

    /// <summary>Деньги фактически выданы гостю (наличными, переводом) — фиксирует сотрудник.</summary>
    public Result MarkPaid(Guid? by, DateTimeOffset now)
    {
        if (Status != RefundStatus.Approved)
        {
            return FinanceErrors.RefundInvalidTransition(Status, "отметить выплату");
        }

        Status = RefundStatus.Paid;
        PaidBy = by;
        PaidAt = now;
        return Result.Success();
    }

    private void Decide(Guid? by, string? comment, DateTimeOffset now)
    {
        DecidedBy = by;
        DecidedAt = now;
        DecisionComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
    }
}

public sealed class ExpenseCategory
{
    private ExpenseCategory()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public ExpenseCategory(Guid hotelId, string? code, string name, int sortOrder)
    {
        Id = Guid.CreateVersion7();
        HotelId = hotelId;
        Code = code ?? Id.ToString("N");
        Name = name.Trim();
        SortOrder = sortOrder;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public Guid HotelId { get; private set; }

    /// <summary>Стабильный код справочника (для категорий по умолчанию).</summary>
    public string Code { get; private set; }

    public string Name { get; private set; }

    public int SortOrder { get; private set; }

    public bool IsActive { get; private set; }

    public uint Version { get; private set; }

    public void Rename(string name, int sortOrder)
    {
        Name = name.Trim();
        SortOrder = sortOrder;
    }

    public void SetActive(bool active) => IsActive = active;
}

/// <summary>Расход гостиницы. Не удаляется: ошибочный аннулируется с причиной.</summary>
public sealed class Expense
{
    private Expense()
    {
        Description = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid HotelId { get; private set; }

    public Guid CategoryId { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>Дата расхода по времени гостиницы.</summary>
    public DateOnly Date { get; private set; }

    public PaymentMethod Method { get; private set; }

    public string? Supplier { get; private set; }

    public string Description { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsVoided { get; private set; }

    public Guid? VoidedBy { get; private set; }

    public DateTimeOffset? VoidedAt { get; private set; }

    public string? VoidReason { get; private set; }

    public uint Version { get; private set; }

    public static Result<Expense> Create(Guid hotelId, Guid categoryId, decimal amount, DateOnly date, PaymentMethod method, string? supplier, string description, Guid? by, DateTimeOffset now)
    {
        if (amount <= 0 || decimal.Round(amount, 2) != amount)
        {
            return FinanceErrors.AmountInvalid;
        }

        var expense = new Expense { Id = Guid.CreateVersion7(), HotelId = hotelId, CreatedBy = by, CreatedAt = now };
        expense.Set(categoryId, amount, date, method, supplier, description, now);
        return expense;
    }

    public Result Update(Guid categoryId, decimal amount, DateOnly date, PaymentMethod method, string? supplier, string description, DateTimeOffset now)
    {
        if (IsVoided)
        {
            return FinanceErrors.ExpenseVoided;
        }

        if (amount <= 0 || decimal.Round(amount, 2) != amount)
        {
            return FinanceErrors.AmountInvalid;
        }

        Set(categoryId, amount, date, method, supplier, description, now);
        return Result.Success();
    }

    public Result Void(string reason, Guid? by, DateTimeOffset now)
    {
        if (IsVoided)
        {
            return FinanceErrors.ExpenseVoided;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return FinanceErrors.ReasonRequired;
        }

        IsVoided = true;
        VoidedBy = by;
        VoidedAt = now;
        VoidReason = reason.Trim();
        UpdatedAt = now;
        return Result.Success();
    }

    private void Set(Guid categoryId, decimal amount, DateOnly date, PaymentMethod method, string? supplier, string description, DateTimeOffset now)
    {
        CategoryId = categoryId;
        Amount = amount;
        Date = date;
        Method = method;
        Supplier = string.IsNullOrWhiteSpace(supplier) ? null : supplier.Trim();
        Description = description.Trim();
        UpdatedAt = now;
    }
}

/// <summary>
/// Баланс брони: к оплате − получено. К оплате: действующая и завершённая бронь — её стоимость;
/// отменённая и неявка — начисленный штраф; истёкшая — ничего.
/// </summary>
public static class BookingBalance
{
    public static decimal Received(IEnumerable<Payment> payments) => payments.Sum(p => p.NetAmount);
}
