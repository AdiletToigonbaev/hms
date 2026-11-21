using System.Text.Json;
using FluentValidation;
using Hms.Modules.Pricing.Contracts;
using Hms.SharedKernel;

namespace Hms.Modules.Bookings.Application;

/// <summary>
/// Время проживания в запросе. Посуточно (Kind = Nightly или не задан): CheckIn, CheckOut.
/// Почасово (Kind = Hourly): Date, StartHour (0–23, начало — целый час), Hours.
/// </summary>
public interface IStayInput
{
    string? Kind { get; }

    DateOnly? CheckIn { get; }

    DateOnly? CheckOut { get; }

    DateOnly? Date { get; }

    int? StartHour { get; }

    int? Hours { get; }
}

public static class StayKinds
{
    public const string Nightly = "Nightly";
    public const string Hourly = "Hourly";

    public static bool IsHourly(this IStayInput input) => string.Equals(input?.Kind, Hourly, StringComparison.OrdinalIgnoreCase);
}

// ---- Подбор и расчёт ----

public sealed record AvailabilityQuery(
    DateOnly? CheckIn,
    DateOnly? CheckOut,
    int? Adults,
    int? Children,
    Guid? RoomTypeId,
    string? Kind = null,
    DateOnly? Date = null,
    int? StartHour = null,
    int? Hours = null) : IStayInput;

/// <summary>Свободный номер с расчётом стоимости (для персонала).</summary>
public sealed record AvailableRoomDto(Guid RoomId, string Number, int? Floor, Guid RoomTypeId, string RoomTypeName, int Capacity, PriceQuote Quote);

/// <summary>Свободная категория (для сайта): без номеров комнат, только количество и цена «от».</summary>
/// <param name="Units">Ночи (посуточно) или часы (почасово).</param>
public sealed record PublicAvailabilityDto(Guid RoomTypeId, string RoomTypeName, int Capacity, int AvailableCount, int Nights, decimal TotalFrom, string Kind = StayKinds.Nightly, int Units = 0);

public sealed record QuoteRequest(
    Guid RoomId,
    DateOnly? CheckIn,
    DateOnly? CheckOut,
    int Adults,
    int Children,
    string? PromoCode,
    IReadOnlyList<ServiceLine>? Services,
    string? Kind = null,
    DateOnly? Date = null,
    int? StartHour = null,
    int? Hours = null) : IStayInput;

public sealed record PublicQuoteRequest(
    Guid RoomTypeId,
    DateOnly? CheckIn,
    DateOnly? CheckOut,
    int Adults,
    int Children,
    string? PromoCode,
    IReadOnlyList<ServiceLine>? Services,
    string? Kind = null,
    DateOnly? Date = null,
    int? StartHour = null,
    int? Hours = null) : IStayInput;

// ---- Бронь ----

public sealed record CreateBookingRequest(
    Guid RoomId,
    DateOnly? CheckIn,
    DateOnly? CheckOut,
    int Adults,
    int Children,
    string GuestName,
    string? GuestPhone,
    string? GuestEmail,
    string? Comment,
    string? PromoCode,
    IReadOnlyList<ServiceLine>? Services,
    bool Confirm,
    string? Kind = null,
    DateOnly? Date = null,
    int? StartHour = null,
    int? Hours = null,
    Guid? GuestId = null) : IStayInput;

public sealed record UpdateBookingRequest(string GuestName, string? GuestPhone, string? GuestEmail, int Adults, int Children, string? Comment, uint? Version);

/// <param name="WaivePenalty">Отменить без штрафа — только с правом booking.cancel.penalty_override.</param>
public sealed record CancelBookingRequest(string Reason, bool WaivePenalty = false);

/// <summary>Неявка гостя. WaivePenalty — без штрафа (право booking.cancel.penalty_override).</summary>
public sealed record NoShowRequest(bool WaivePenalty = false, uint? Version = null);

/// <summary>Условия отмены сейчас: бесплатно ли, до какого времени, какой штраф.</summary>
/// <param name="FreeUntil">До какого момента отмена бесплатна (время гостиницы), если бронь подтверждена.</param>
/// <param name="Unit">Hour или Night — в чём считается штраф.</param>
/// <param name="CanWaive">Может ли текущий сотрудник отменить без штрафа.</param>
/// <param name="NoShowPenalty">Штраф за неявку по правилам (для кнопки «Неявка»).</param>
public sealed record CancellationQuoteDto(bool IsFree, string? FreeUntil, decimal Penalty, int Units, string Unit, bool CanWaive, decimal NoShowPenalty);

/// <summary>
/// Перенос брони: новое время (вид брони тот же) и номер. Посуточно — CheckIn/CheckOut; почасово — Date, StartHour, Hours.
/// </summary>
public sealed record RescheduleBookingRequest(
    Guid RoomId,
    string? Kind,
    DateOnly? CheckIn,
    DateOnly? CheckOut,
    DateOnly? Date,
    int? StartHour,
    int? Hours,
    uint? Version = null) : IStayInput;

/// <summary>Что получится после переноса: новое время, номер и цена по текущим ценам.</summary>
public sealed record RescheduleQuoteDto(string StartsAt, string EndsAt, Guid RoomId, string RoomNumber, decimal OldTotal, decimal NewTotal, PriceQuote Price);

/// <summary>Продление: на сколько часов (почасовая) или ночей (посуточная). Version — защита от двойного нажатия.</summary>
public sealed record ExtendBookingRequest(int Units, uint? Version);

/// <param name="MaxUnits">Сколько максимум можно добавить (номер дальше занят); null — без ограничения в пределах правил.</param>
/// <param name="NextBookingStartsAt">Когда начинается следующая бронь номера (время гостиницы), если она ограничивает продление.</param>
public sealed record ExtensionQuoteDto(
    string Kind,
    int Units,
    string NewEndsAt,
    decimal Amount,
    PriceQuote Price,
    int? MaxUnits,
    string? NextBookingStartsAt);

public sealed record BookingRoomDto(Guid RoomId, string Number, Guid RoomTypeId, string RoomTypeName);

public sealed record BookingServiceDto(Guid ServiceId, string Name, int Quantity, decimal UnitPrice, bool PerNight, decimal Total);

public sealed record BookingExtensionDto(string Kind, int Quantity, string PreviousEndsAt, string NewEndsAt, decimal Amount, string? CreatedByName, DateTimeOffset CreatedAt);

/// <param name="StartsAt">Начало по времени гостиницы, «yyyy-MM-ddTHH:mm».</param>
/// <param name="EndsAt">Выезд по времени гостиницы.</param>
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
    uint Version,
    string Kind,
    string StartsAt,
    string EndsAt,
    int? Hours,
    decimal ExtensionsAmount,
    IReadOnlyList<BookingExtensionDto> Extensions,
    Guid? GuestId = null,
    string? HoldUntil = null,
    decimal PenaltyAmount = 0m,
    string? PenaltyKind = null,
    bool PenaltyWaived = false);

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
    DateTimeOffset CreatedAt,
    string Kind,
    string StartsAt,
    string EndsAt,
    int? Hours,
    Guid? GuestId = null);

// ---- Календарь ----

/// <summary>Период календаря (даты гостиницы, включительно), не длиннее 31 дня.</summary>
public sealed record CalendarQuery(DateOnly? From, DateOnly? To);

public sealed record CalendarRoomDto(Guid Id, string Number, int? Floor, Guid RoomTypeId, string RoomTypeName, int Capacity, string Status, bool SellsHourly, bool IsSellable);

/// <summary>Бронь на шкале. Время — по гостинице, «yyyy-MM-ddTHH:mm»; BlockedUntil — конец уборки после выезда.</summary>
/// <param name="NoShowDue">Гость опаздывает: время неявки по правилам прошло, а бронь всё ещё ждёт заселения.</param>
public sealed record CalendarBookingDto(
    Guid Id,
    string Number,
    Guid RoomId,
    string Status,
    string Kind,
    string StartsAt,
    string EndsAt,
    string BlockedUntil,
    string GuestName,
    string? GuestPhone,
    int Adults,
    int Children,
    int? Hours,
    int Nights,
    decimal TotalAmount,
    string? HoldUntil,
    bool NoShowDue,
    bool CanReschedule,
    uint Version,
    bool CanExtend = false);

/// <param name="Now">Текущее время гостиницы — для линии «сейчас».</param>
public sealed record CalendarDto(
    DateOnly From,
    DateOnly To,
    string Now,
    int CleaningBufferMinutes,
    int HourlyMinHours,
    int HourlyMaxHours,
    string CheckInTime,
    string CheckOutTime,
    IReadOnlyList<CalendarRoomDto> Rooms,
    IReadOnlyList<CalendarBookingDto> Bookings);

public interface ICalendarService
{
    Task<Result<CalendarDto>> GetAsync(CalendarQuery query, CancellationToken ct);
}

/// <summary>Итог прохода фоновой задачи.</summary>
public sealed record LifecycleRun(int Expired, int NoShows);

/// <summary>
/// Фоновые правила брони: истечение неподтверждённых (HoldUntil) и автоматическая неявка (если включена владельцем).
/// Вызывается фоновой службой раз в минуту; <paramref name="now"/> — для тестов.
/// </summary>
public interface IBookingLifecycle
{
    Task<LifecycleRun> RunAsync(DateTimeOffset now, CancellationToken ct);
}

public sealed record BookingQuery(string? Status, DateOnly? From, DateOnly? To, Guid? RoomId, string? Search, int? Page, int? PageSize, string? Kind = null);

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

    Task<Result<CancellationQuoteDto>> QuoteCancellationAsync(Guid id, CancellationToken ct);

    Task<Result<BookingDto>> MarkNoShowAsync(Guid id, NoShowRequest request, CancellationToken ct);

    Task<Result<RescheduleQuoteDto>> QuoteRescheduleAsync(Guid id, RescheduleBookingRequest request, CancellationToken ct);

    Task<Result<BookingDto>> RescheduleAsync(Guid id, RescheduleBookingRequest request, CancellationToken ct);

    Task<Result<ExtensionQuoteDto>> QuoteExtensionAsync(Guid id, int units, CancellationToken ct);

    Task<Result<BookingDto>> ExtendAsync(Guid id, ExtendBookingRequest request, CancellationToken ct);

    Task<Result<IReadOnlyList<BookingHistoryDto>>> GetHistoryAsync(Guid id, CancellationToken ct);
}

public static class BookingErrors
{
    public const int MaxNights = 60;
    public const int MaxExtendNights = 30;

    public static readonly Error NotFound = Error.NotFound("booking.not_found", "Бронь не найдена.");
    public static readonly Error RoomUnavailable = Error.Conflict("booking.room_unavailable", "Номер на это время уже занят.");
    public static readonly Error RoomNotSellable = Error.BusinessRule("booking.room_not_sellable", "Номер не продаётся: в архиве или «вне продажи».");
    public static readonly Error RoomNotHourly = Error.BusinessRule("booking.room_not_hourly", "Номер не продаётся по часам: у категории и номера не задана цена часа.");
    public static readonly Error RoomNotFound = Error.NotFound("booking.room_not_found", "Номер не найден.");
    public static readonly Error RoomTypeUnavailable = Error.Conflict("booking.room_type_unavailable", "Свободных номеров этой категории на это время нет.");
    public static readonly Error PastDates = Error.BusinessRule("booking.past_dates", "Дата заезда уже прошла.");
    public static readonly Error StartInPast = Error.BusinessRule("booking.start_in_past", "Время начала уже прошло.");
    public static readonly Error ConcurrencyConflict = Error.Conflict("concurrency.conflict", "Бронь изменил другой пользователь. Обновите страницу и повторите.");

    public static Error CapacityExceeded(int capacity) =>
        Error.BusinessRule("booking.capacity_exceeded", $"Номер рассчитан не более чем на {capacity} гост.");

    public static Error HoursOutOfRange(int min, int max) =>
        Error.BusinessRule("booking.hours_out_of_range", $"Почасовая бронь — от {min} до {max} ч. Дольше — оформите посуточно.");

    public static Error ExtensionConflict(string until) =>
        Error.Conflict("booking.extension_conflict", $"Номер дальше занят — продлить можно не позже чем до {until}.");

    public static readonly Error PenaltyWaiveForbidden =
        Error.Forbidden("booking.penalty_waive_forbidden", "Отменить без штрафа может только сотрудник с правом «Отмена без штрафа».");

    public static readonly Error CalendarRange = Error.Validation("calendar.range", "Период календаря — от 1 до 31 дня.");

    public static Error ExtensionTooLong(int max) =>
        Error.BusinessRule("booking.extension_too_long", $"Почасовая бронь не может быть длиннее {max} ч. Продлите посуточно новой бронью.");
}

// ---- Валидация ----

internal static class StayRules
{
    public static void Apply<T>(AbstractValidator<T> v)
        where T : IStayInput
    {
        v.RuleFor(x => x.Kind).Must(k => k is null || k.Equals(StayKinds.Nightly, StringComparison.OrdinalIgnoreCase) || k.Equals(StayKinds.Hourly, StringComparison.OrdinalIgnoreCase))
            .WithMessage("Вид брони: по суткам или по часам.");

        v.When(x => !x.IsHourly(), () =>
        {
            v.RuleFor(x => x.CheckIn).NotNull().WithMessage("Укажите дату заезда.");
            v.RuleFor(x => x.CheckOut).NotNull().WithMessage("Укажите дату выезда.")
                .GreaterThan(x => x.CheckIn).When(x => x.CheckIn is not null).WithMessage("Дата выезда должна быть позже даты заезда.");
            v.RuleFor(x => x).Must(x => x.CheckIn is null || x.CheckOut is null || x.CheckOut.Value.DayNumber - x.CheckIn.Value.DayNumber <= BookingErrors.MaxNights)
                .OverridePropertyName("checkOut").WithMessage($"Не более {BookingErrors.MaxNights} ночей в одной брони.");
        });

        v.When(x => x.IsHourly(), () =>
        {
            v.RuleFor(x => x.Date).NotNull().WithMessage("Укажите дату.");
            v.RuleFor(x => x.StartHour).NotNull().WithMessage("Укажите время начала.")
                .InclusiveBetween(0, 23).WithMessage("Время начала — целый час от 00:00 до 23:00.");
            v.RuleFor(x => x.Hours).NotNull().WithMessage("Укажите количество часов.")
                .InclusiveBetween(1, 24).WithMessage("От 1 до 24 часов.");
        });
    }
}

public sealed class AvailabilityQueryValidator : AbstractValidator<AvailabilityQuery>
{
    public AvailabilityQueryValidator()
    {
        StayRules.Apply(this);
        RuleFor(x => x.Adults).InclusiveBetween(1, 20).When(x => x.Adults is not null);
        RuleFor(x => x.Children).InclusiveBetween(0, 20).When(x => x.Children is not null);
    }
}

public sealed class QuoteRequestValidator : AbstractValidator<QuoteRequest>
{
    public QuoteRequestValidator()
    {
        RuleFor(x => x.RoomId).NotEmpty();
        StayRules.Apply(this);
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
        StayRules.Apply(this);
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
        StayRules.Apply(this);
        RuleFor(x => x.Adults).InclusiveBetween(1, 20).WithMessage("Взрослых — от 1 до 20.");
        RuleFor(x => x.Children).InclusiveBetween(0, 20).WithMessage("Детей — от 0 до 20.");
        // Выбран гость из базы — контакт берётся из карточки; иначе нужен контакт нового гостя.
        RuleFor(x => x.GuestName).NotEmpty().When(x => x.GuestId is null).WithMessage("Укажите имя гостя.").MaximumLength(200);
        RuleFor(x => x.GuestPhone).Must(v => PhoneNumber.Parse(v).IsSuccess).When(x => !string.IsNullOrWhiteSpace(x.GuestPhone))
            .WithMessage("Некорректный номер телефона. Пример: +996 XXX XXX XXX.");
        RuleFor(x => x.GuestEmail).EmailAddress().WithMessage("Некорректный email.").MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.GuestEmail));
        RuleFor(x => x).Must(x => x.GuestId is not null || !string.IsNullOrWhiteSpace(x.GuestPhone) || !string.IsNullOrWhiteSpace(x.GuestEmail))
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

public sealed class RescheduleBookingRequestValidator : AbstractValidator<RescheduleBookingRequest>
{
    public RescheduleBookingRequestValidator()
    {
        RuleFor(x => x.RoomId).NotEmpty().WithMessage("Выберите номер.");
        StayRules.Apply(this);
    }
}

public sealed class ExtendBookingRequestValidator : AbstractValidator<ExtendBookingRequest>
{
    public ExtendBookingRequestValidator() =>
        RuleFor(x => x.Units).InclusiveBetween(1, BookingErrors.MaxExtendNights).WithMessage($"Продление — от 1 до {BookingErrors.MaxExtendNights}.");
}
