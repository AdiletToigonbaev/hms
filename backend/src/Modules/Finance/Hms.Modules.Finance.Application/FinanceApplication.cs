using FluentValidation;
using Hms.Modules.Finance.Contracts;
using Hms.Modules.Finance.Domain;
using Hms.SharedKernel;

namespace Hms.Modules.Finance.Application;

// ---- Платежи ----

/// <summary>Ручная регистрация полученных денег. Без брони — только «Прочее» (например, мини-бар без брони).</summary>
public sealed record RegisterPaymentRequest(Guid? BookingId, string Type, string Method, decimal Amount, string? Comment);

public sealed record ReversePaymentRequest(string Reason);

public sealed record PaymentQuery(DateOnly? From, DateOnly? To, string? Method, string? Type, Guid? BookingId, int? Page, int? PageSize);

/// <param name="PaidAt">Время гостиницы «yyyy-MM-ddTHH:mm».</param>
public sealed record PaymentListItemDto(
    Guid Id,
    string Type,
    string Method,
    string Channel,
    decimal Amount,
    string Status,
    decimal RefundedAmount,
    string PaidAt,
    Guid? BookingId,
    string? BookingNumber,
    string? GuestName,
    string? RegisteredByName,
    string? Comment,
    Guid? ReversalOfPaymentId);

public sealed record PaymentTransactionDto(Guid Id, string Kind, decimal Amount, string Status, string OccurredAt, string? ByName, string? Comment);

public sealed record PaymentDto(PaymentListItemDto Payment, IReadOnlyList<PaymentTransactionDto> Transactions, IReadOnlyList<RefundDto> Refunds, IReadOnlyList<string> AllowedActions, uint Version);

/// <summary>Итоги по выборке: всего и по способам оплаты («касса»).</summary>
public sealed record PaymentTotalsDto(decimal Total, IReadOnlyDictionary<string, decimal> ByMethod, int Count);

public sealed record PaymentPageDto(PagedResult<PaymentListItemDto> Page, PaymentTotalsDto Totals);

/// <summary>Баланс брони: к оплате, получено, возвращено, остаток (больше нуля — долг, меньше — переплата).</summary>
/// <param name="PrepaymentRequired">Предоплата по настройке гостиницы (0 — не требуется).</param>
public sealed record BookingBalanceDto(
    Guid BookingId,
    string Number,
    string BookingStatus,
    decimal Due,
    decimal Received,
    decimal Refunded,
    decimal Balance,
    decimal PrepaymentRequired,
    IReadOnlyList<PaymentListItemDto> Payments,
    IReadOnlyList<RefundDto> Refunds);

/// <summary>Строка списка долгов: бронь с остатком к оплате (или переплатой).</summary>
public sealed record DebtDto(
    Guid BookingId,
    string Number,
    string BookingStatus,
    string Kind,
    string GuestName,
    string? RoomNumber,
    string StartsAt,
    string EndsAt,
    decimal Due,
    decimal Received,
    decimal Balance);

/// <param name="Scope">current — долги по начавшимся и закрытым броням; upcoming — не оплаченные будущие; overpaid — переплата (нужен возврат).</param>
public sealed record DebtQuery(string? Scope);

public sealed record DebtListDto(IReadOnlyList<DebtDto> Items, decimal Total);

// ---- Возвраты ----

public sealed record RequestRefundRequest(Guid PaymentId, decimal Amount, string Reason, string Method);

public sealed record DecideRefundRequest(string? Comment);

public sealed record RefundQuery(string? Status, int? Page, int? PageSize);

public sealed record RefundDto(
    Guid Id,
    Guid PaymentId,
    Guid? BookingId,
    string? BookingNumber,
    decimal Amount,
    string Reason,
    string Method,
    string Status,
    string RequestedAt,
    string? RequestedByName,
    string? DecidedAt,
    string? DecidedByName,
    string? DecisionComment,
    string? PaidAt,
    string? PaidByName,
    uint Version);

/// <param name="CanApprove">Может ли текущий сотрудник утверждать возвраты.</param>
/// <param name="ApproveLimit">Его лимит (null — без лимита).</param>
public sealed record RefundPageDto(PagedResult<RefundDto> Page, bool CanApprove, decimal? ApproveLimit);

// ---- Расходы ----

public sealed record SaveExpenseRequest(Guid CategoryId, decimal Amount, DateOnly Date, string Method, string? Supplier, string Description, uint? Version);

public sealed record VoidExpenseRequest(string Reason);

public sealed record ExpenseQuery(DateOnly? From, DateOnly? To, Guid? CategoryId, bool? IncludeVoided, int? Page, int? PageSize);

public sealed record ExpenseDto(
    Guid Id,
    Guid CategoryId,
    string CategoryName,
    decimal Amount,
    DateOnly Date,
    string Method,
    string? Supplier,
    string Description,
    string? CreatedByName,
    DateTimeOffset CreatedAt,
    bool IsVoided,
    string? VoidReason,
    string? VoidedByName,
    uint Version);

/// <summary>Итоги расходов выборки: всего и по категориям.</summary>
public sealed record ExpensePageDto(PagedResult<ExpenseDto> Page, decimal Total, IReadOnlyDictionary<string, decimal> ByCategory, bool CanManage);

public sealed record SaveExpenseCategoryRequest(string Name, int SortOrder);

public sealed record ExpenseCategoryDto(Guid Id, string Name, int SortOrder, bool IsActive);

// ---- Сервисы ----

public interface IPaymentService
{
    Task<PaymentPageDto> ListAsync(PaymentQuery query, CancellationToken ct);

    Task<Result<PaymentDto>> GetAsync(Guid id, CancellationToken ct);

    Task<Result<PaymentDto>> RegisterAsync(RegisterPaymentRequest request, CancellationToken ct);

    Task<Result<PaymentDto>> ReverseAsync(Guid id, ReversePaymentRequest request, CancellationToken ct);

    Task<Result<BookingBalanceDto>> GetBookingBalanceAsync(Guid bookingId, CancellationToken ct);

    Task<DebtListDto> ListDebtsAsync(DebtQuery query, CancellationToken ct);
}

public interface IRefundService
{
    Task<RefundPageDto> ListAsync(RefundQuery query, CancellationToken ct);

    Task<Result<RefundDto>> GetAsync(Guid id, CancellationToken ct);

    Task<Result<RefundDto>> RequestAsync(RequestRefundRequest request, CancellationToken ct);

    Task<Result<RefundDto>> ApproveAsync(Guid id, DecideRefundRequest request, CancellationToken ct);

    Task<Result<RefundDto>> RejectAsync(Guid id, DecideRefundRequest request, CancellationToken ct);

    Task<Result<RefundDto>> MarkPaidAsync(Guid id, CancellationToken ct);
}

public interface IExpenseService
{
    Task<ExpensePageDto> ListAsync(ExpenseQuery query, CancellationToken ct);

    Task<Result<ExpenseDto>> GetAsync(Guid id, CancellationToken ct);

    Task<Result<ExpenseDto>> CreateAsync(SaveExpenseRequest request, CancellationToken ct);

    Task<Result<ExpenseDto>> UpdateAsync(Guid id, SaveExpenseRequest request, CancellationToken ct);

    Task<Result<ExpenseDto>> VoidAsync(Guid id, VoidExpenseRequest request, CancellationToken ct);

    Task<IReadOnlyList<ExpenseCategoryDto>> ListCategoriesAsync(CancellationToken ct);

    Task<Result<ExpenseCategoryDto>> SaveCategoryAsync(Guid? id, SaveExpenseCategoryRequest request, CancellationToken ct);

    Task<Result<ExpenseCategoryDto>> SetCategoryActiveAsync(Guid id, bool active, CancellationToken ct);
}

public static class PaymentAppErrors
{
    public static readonly Error BookingNotFound = Error.NotFound("payment.booking_not_found", "Бронь не найдена.");
    public static readonly Error BookingRequired = Error.Validation("payment.booking_required", "Оплату проживания, предоплату, услуги и штраф регистрируйте по брони.");
    public static readonly Error NothingDue = Error.BusinessRule("payment.nothing_due", "По этой брони нечего оплачивать.");
    public static readonly Error ConcurrencyConflict = Error.Conflict("concurrency.conflict", "Данные изменил другой пользователь. Обновите страницу и повторите.");

    public static Error Overpayment(decimal balance) =>
        Error.BusinessRule("payment.overpayment", $"Сумма больше остатка к оплате по брони ({balance:0.##} сом).");
}

// ---- Валидация ----

internal static class Enums
{
    public static bool Is<T>(string? value)
        where T : struct, Enum => Enum.TryParse<T>(value, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed);
}

public sealed class RegisterPaymentRequestValidator : AbstractValidator<RegisterPaymentRequest>
{
    public RegisterPaymentRequestValidator()
    {
        RuleFor(x => x.Type).Must(t => Enums.Is<PaymentType>(t) && t != nameof(PaymentType.Reversal)).WithMessage("Выберите, за что платёж.");
        RuleFor(x => x.Method).Must(Enums.Is<PaymentMethod>).WithMessage("Выберите способ оплаты.");
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Сумма должна быть больше нуля.")
            .LessThanOrEqualTo(10_000_000).WithMessage("Слишком большая сумма.")
            .Must(a => decimal.Round(a, 2) == a).WithMessage("Не больше двух знаков после запятой.");
        RuleFor(x => x.Comment).MaximumLength(500);
    }
}

public sealed class ReversePaymentRequestValidator : AbstractValidator<ReversePaymentRequest>
{
    public ReversePaymentRequestValidator() => RuleFor(x => x.Reason).NotEmpty().WithMessage("Укажите причину аннулирования.").MaximumLength(500);
}

public sealed class RequestRefundRequestValidator : AbstractValidator<RequestRefundRequest>
{
    public RequestRefundRequestValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Сумма должна быть больше нуля.")
            .Must(a => decimal.Round(a, 2) == a).WithMessage("Не больше двух знаков после запятой.");
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Укажите причину возврата.").MaximumLength(500);
        RuleFor(x => x.Method).Must(Enums.Is<PaymentMethod>).WithMessage("Выберите, как вернуть деньги.");
    }
}

public sealed class DecideRefundRequestValidator : AbstractValidator<DecideRefundRequest>
{
    public DecideRefundRequestValidator() => RuleFor(x => x.Comment).MaximumLength(500);
}

public sealed class SaveExpenseRequestValidator : AbstractValidator<SaveExpenseRequest>
{
    public SaveExpenseRequestValidator()
    {
        RuleFor(x => x.CategoryId).NotEmpty().WithMessage("Выберите категорию.");
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Сумма должна быть больше нуля.")
            .LessThanOrEqualTo(100_000_000).WithMessage("Слишком большая сумма.")
            .Must(a => decimal.Round(a, 2) == a).WithMessage("Не больше двух знаков после запятой.");
        RuleFor(x => x.Method).Must(Enums.Is<PaymentMethod>).WithMessage("Выберите способ оплаты.");
        RuleFor(x => x.Supplier).MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().WithMessage("Опишите расход.").MaximumLength(500);
    }
}

public sealed class VoidExpenseRequestValidator : AbstractValidator<VoidExpenseRequest>
{
    public VoidExpenseRequestValidator() => RuleFor(x => x.Reason).NotEmpty().WithMessage("Укажите причину аннулирования.").MaximumLength(500);
}

public sealed class SaveExpenseCategoryRequestValidator : AbstractValidator<SaveExpenseCategoryRequest>
{
    public SaveExpenseCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Укажите название.").MaximumLength(100);
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 1000);
    }
}

// ---- Кабинет гостя: свои оплаты и возвраты (Sprint 8) ----

/// <summary>Платёж в кабинете гостя — без служебных данных (кто принял, журнал операций). Время — по гостинице.</summary>
public sealed record MyPaymentDto(Guid Id, Guid? BookingId, string? BookingNumber, string Type, string Method, decimal Amount, decimal RefundedAmount, string Status, string PaidAt);

public sealed record MyRefundDto(Guid Id, Guid? BookingId, string? BookingNumber, decimal Amount, string Reason, string Status, string RequestedAt, string? DecidedAt, string? DecisionComment, string? PaidAt);

/// <summary>Баланс своей брони. <paramref name="Refundable"/> — сколько ещё можно запросить к возврату (переплата минус уже запрошенное).</summary>
public sealed record MyBookingBalanceDto(
    Guid BookingId,
    decimal Due,
    decimal Received,
    decimal Refunded,
    decimal Balance,
    decimal PrepaymentRequired,
    decimal Refundable,
    IReadOnlyList<MyPaymentDto> Payments,
    IReadOnlyList<MyRefundDto> Refunds);

/// <summary>Запрос возврата гостем: по своей брони, не больше переплаты. Способ возврата — как у оплаты.</summary>
public sealed record MyRefundRequest(Guid BookingId, decimal Amount, string Reason);

public interface IMyFinanceService
{
    Task<IReadOnlyList<MyPaymentDto>> PaymentsAsync(CancellationToken ct);

    Task<IReadOnlyList<MyRefundDto>> RefundsAsync(CancellationToken ct);

    Task<Result<MyBookingBalanceDto>> BalanceAsync(Guid bookingId, CancellationToken ct);

    Task<Result<IReadOnlyList<MyRefundDto>>> RequestRefundAsync(MyRefundRequest request, CancellationToken ct);
}

public sealed class MyRefundRequestValidator : AbstractValidator<MyRefundRequest>
{
    public MyRefundRequestValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Сумма должна быть больше нуля.")
            .Must(a => decimal.Round(a, 2) == a).WithMessage("Не больше двух знаков после запятой.");
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Укажите причину возврата.").MaximumLength(500);
    }
}
