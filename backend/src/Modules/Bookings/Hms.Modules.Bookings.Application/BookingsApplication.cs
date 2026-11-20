using System.Text.Json;
using FluentValidation;
using Hms.Modules.Pricing.Contracts;
using Hms.SharedKernel;

namespace Hms.Modules.Bookings.Application;

// ---- Подбор и расчёт ----

public sealed record AvailabilityQuery(DateOnly CheckIn, DateOnly CheckOut, int? Adults, int? Children, Guid? RoomTypeId);

/// <summary>Свободный номер с расчётом стоимости (для персонала).</summary>
public sealed record AvailableRoomDto(Guid RoomId, string Number, int? Floor, Guid RoomTypeId, string RoomTypeName, int Capacity, PriceQuote Quote);

/// <summary>Свободная категория (для сайта): без номеров комнат, только количество и цена «от».</summary>
public sealed record PublicAvailabilityDto(Guid RoomTypeId, string RoomTypeName, int Capacity, int AvailableCount, int Nights, decimal TotalFrom);

public sealed record QuoteRequest(Guid RoomId, DateOnly CheckIn, DateOnly CheckOut, int Adults, int Children, string? PromoCode, IReadOnlyList<ServiceLine>? Services);

public sealed record PublicQuoteRequest(Guid RoomTypeId, DateOnly CheckIn, DateOnly CheckOut, int Adults, int Children, string? PromoCode, IReadOnlyList<ServiceLine>? Services);

// ---- Бронь ----

public sealed record CreateBookingRequest(
    Guid RoomId,
    DateOnly CheckIn,
    DateOnly CheckOut,
    int Adults,
    int Children,
    string GuestName,
    string? GuestPhone,
    string? GuestEmail,
    string? Comment,
    string? PromoCode,
    IReadOnlyList<ServiceLine>? Services,
    bool Confirm);

public sealed record UpdateBookingRequest(string GuestName, string? GuestPhone, string? GuestEmail, int Adults, int Children, string? Comment, uint? Version);

public sealed record CancelBookingRequest(string Reason);

public sealed record BookingRoomDto(Guid RoomId, string Number, Guid RoomTypeId, string RoomTypeName);

public sealed record BookingServiceDto(Guid ServiceId, string Name, int Quantity, decimal UnitPrice, bool PerNight, decimal Total);

public sealed record BookingDto(
    Guid Id,
    string Number,
    string Status,
    string Source,
    DateOnly CheckIn,
    DateOnly CheckOut,
    int Nights,
    int Adults,
    int Children,
    string GuestName,
    string? GuestPhone,
    string? GuestEmail,
    string? Comment,
    BookingRoomDto Room,
    decimal AccommodationAmount,
    decimal DiscountAmount,
    decimal ServicesAmount,
    decimal TotalAmount,
    string? PromoCode,
    PriceQuote? Price,
    IReadOnlyList<BookingServiceDto> Services,
    IReadOnlyList<string> AllowedActions,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? CancelledAt,
    string? CancelReason,
    DateTimeOffset CreatedAt,
    uint Version);

public sealed record BookingListItemDto(
    Guid Id,
    string Number,
    string Status,
    DateOnly CheckIn,
    DateOnly CheckOut,
    int Nights,
    string GuestName,
    string? GuestPhone,
    string RoomNumber,
    string RoomTypeName,
    decimal TotalAmount,
    DateTimeOffset CreatedAt);

public sealed record BookingQuery(string? Status, DateOnly? From, DateOnly? To, Guid? RoomId, string? Search, int? Page, int? PageSize);

public sealed record BookingHistoryDto(
    Guid Id,
    string EventType,
    JsonElement? OldValues,
    JsonElement? NewValues,
    Guid? ChangedBy,
    string? ChangedByName,
    DateTimeOffset ChangedAt,
    string? Comment);

public interface IAvailabilityService
{
    Task<Result<IReadOnlyList<AvailableRoomDto>>> FindRoomsAsync(AvailabilityQuery query, CancellationToken ct);

    Task<Result<IReadOnlyList<PublicAvailabilityDto>>> FindPublicAsync(AvailabilityQuery query, CancellationToken ct);

    Task<Result<PriceQuote>> QuoteAsync(QuoteRequest request, CancellationToken ct);

    Task<Result<PriceQuote>> QuotePublicAsync(PublicQuoteRequest request, CancellationToken ct);
}

public interface IBookingService
{
    Task<PagedResult<BookingListItemDto>> ListAsync(BookingQuery query, CancellationToken ct);

    Task<Result<BookingDto>> GetAsync(Guid id, CancellationToken ct);

    Task<Result<BookingDto>> CreateAsync(CreateBookingRequest request, CancellationToken ct);

    Task<Result<BookingDto>> UpdateAsync(Guid id, UpdateBookingRequest request, CancellationToken ct);

    Task<Result<BookingDto>> ConfirmAsync(Guid id, CancellationToken ct);

    Task<Result<BookingDto>> CancelAsync(Guid id, CancelBookingRequest request, CancellationToken ct);

    Task<Result<IReadOnlyList<BookingHistoryDto>>> GetHistoryAsync(Guid id, CancellationToken ct);
}

public static class BookingErrors
{
    public const int MaxNights = 60;

    public static readonly Error NotFound = Error.NotFound("booking.not_found", "Бронь не найдена.");
    public static readonly Error RoomUnavailable = Error.Conflict("booking.room_unavailable", "Номер на эти даты уже занят.");
    public static readonly Error RoomNotSellable = Error.BusinessRule("booking.room_not_sellable", "Номер не продаётся: в архиве или «вне продажи».");
    public static readonly Error RoomNotFound = Error.NotFound("booking.room_not_found", "Номер не найден.");
    public static readonly Error RoomTypeUnavailable = Error.Conflict("booking.room_type_unavailable", "Свободных номеров этой категории на эти даты нет.");
    public static readonly Error PastDates = Error.BusinessRule("booking.past_dates", "Дата заезда уже прошла.");
    public static readonly Error ConcurrencyConflict = Error.Conflict("concurrency.conflict", "Бронь изменил другой пользователь. Обновите страницу и повторите.");

    public static Error CapacityExceeded(int capacity) =>
        Error.BusinessRule("booking.capacity_exceeded", $"Номер рассчитан не более чем на {capacity} гост.");
}

// ---- Валидация ----

internal static class StayRules
{
    public static void Apply<T>(AbstractValidator<T> v, Func<T, DateOnly> checkIn, Func<T, DateOnly> checkOut)
    {
        v.RuleFor(x => checkOut(x)).GreaterThan(x => checkIn(x)).OverridePropertyName("checkOut")
            .WithMessage("Дата выезда должна быть позже даты заезда.");
        v.RuleFor(x => x).Must(x => checkOut(x).DayNumber - checkIn(x).DayNumber <= BookingErrors.MaxNights).OverridePropertyName("checkOut")
            .WithMessage($"Не более {BookingErrors.MaxNights} ночей в одной брони.");
    }
}

public sealed class AvailabilityQueryValidator : AbstractValidator<AvailabilityQuery>
{
    public AvailabilityQueryValidator()
    {
        StayRules.Apply(this, x => x.CheckIn, x => x.CheckOut);
        RuleFor(x => x.Adults).InclusiveBetween(1, 20).When(x => x.Adults is not null);
        RuleFor(x => x.Children).InclusiveBetween(0, 20).When(x => x.Children is not null);
    }
}

public sealed class QuoteRequestValidator : AbstractValidator<QuoteRequest>
{
    public QuoteRequestValidator()
    {
        RuleFor(x => x.RoomId).NotEmpty();
        StayRules.Apply(this, x => x.CheckIn, x => x.CheckOut);
        RuleFor(x => x.Adults).InclusiveBetween(1, 20).WithMessage("Взрослых — от 1 до 20.");
        RuleFor(x => x.Children).InclusiveBetween(0, 20);
        RuleForEach(x => x.Services).ChildRules(s => s.RuleFor(l => l.Quantity).InclusiveBetween(1, 50));
    }
}

public sealed class PublicQuoteRequestValidator : AbstractValidator<PublicQuoteRequest>
{
    public PublicQuoteRequestValidator()
    {
        RuleFor(x => x.RoomTypeId).NotEmpty();
        StayRules.Apply(this, x => x.CheckIn, x => x.CheckOut);
        RuleFor(x => x.Adults).InclusiveBetween(1, 20).WithMessage("Взрослых — от 1 до 20.");
        RuleFor(x => x.Children).InclusiveBetween(0, 20);
        RuleForEach(x => x.Services).ChildRules(s => s.RuleFor(l => l.Quantity).InclusiveBetween(1, 50));
    }
}

public sealed class CreateBookingRequestValidator : AbstractValidator<CreateBookingRequest>
{
    public CreateBookingRequestValidator()
    {
        RuleFor(x => x.RoomId).NotEmpty().WithMessage("Выберите номер.");
        StayRules.Apply(this, x => x.CheckIn, x => x.CheckOut);
        RuleFor(x => x.Adults).InclusiveBetween(1, 20).WithMessage("Взрослых — от 1 до 20.");
        RuleFor(x => x.Children).InclusiveBetween(0, 20).WithMessage("Детей — от 0 до 20.");
        RuleFor(x => x.GuestName).NotEmpty().WithMessage("Укажите имя гостя.").MaximumLength(200);
        RuleFor(x => x.GuestPhone).Must(v => PhoneNumber.Parse(v).IsSuccess).When(x => !string.IsNullOrWhiteSpace(x.GuestPhone))
            .WithMessage("Некорректный номер телефона. Пример: +996 555 123 456.");
        RuleFor(x => x.GuestEmail).EmailAddress().WithMessage("Некорректный email.").MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.GuestEmail));
        RuleFor(x => x).Must(x => !string.IsNullOrWhiteSpace(x.GuestPhone) || !string.IsNullOrWhiteSpace(x.GuestEmail))
            .OverridePropertyName("guestPhone").WithMessage("Укажите телефон или email гостя для связи.");
        RuleFor(x => x.Comment).MaximumLength(1000);
        RuleFor(x => x.PromoCode).MaximumLength(30);
        RuleForEach(x => x.Services).ChildRules(s => s.RuleFor(l => l.Quantity).InclusiveBetween(1, 50).WithMessage("Количество — от 1 до 50."));
    }
}

public sealed class UpdateBookingRequestValidator : AbstractValidator<UpdateBookingRequest>
{
    public UpdateBookingRequestValidator()
    {
        RuleFor(x => x.GuestName).NotEmpty().WithMessage("Укажите имя гостя.").MaximumLength(200);
        RuleFor(x => x.GuestPhone).Must(v => PhoneNumber.Parse(v).IsSuccess).When(x => !string.IsNullOrWhiteSpace(x.GuestPhone))
            .WithMessage("Некорректный номер телефона. Пример: +996 555 123 456.");
        RuleFor(x => x.GuestEmail).EmailAddress().WithMessage("Некорректный email.").When(x => !string.IsNullOrWhiteSpace(x.GuestEmail));
        RuleFor(x => x.Adults).InclusiveBetween(1, 20);
        RuleFor(x => x.Children).InclusiveBetween(0, 20);
        RuleFor(x => x.Comment).MaximumLength(1000);
    }
}

public sealed class CancelBookingRequestValidator : AbstractValidator<CancelBookingRequest>
{
    public CancelBookingRequestValidator() => RuleFor(x => x.Reason).NotEmpty().WithMessage("Укажите причину отмены.").MaximumLength(500);
}
