using FluentValidation;
using Hms.Modules.Guests.Domain;
using Hms.SharedKernel;

namespace Hms.Modules.Guests.Application;

/// <param name="BookedAmount">Сумма действующих и завершённых броней.</param>
/// <param name="PaidAmount">Фактически оплачено: поступления − сторно − выплаченные возвраты (модуль Finance).</param>
public sealed record GuestStatsDto(int BookingsCount, int VisitsCount, decimal BookedAmount, DateTimeOffset? LastStayAt, decimal PaidAmount = 0m);

public sealed record GuestListItemDto(
    Guid Id,
    string FullName,
    string? Phone,
    string? Email,
    bool HasAccount,
    bool WhatsAppConsent,
    bool IsAnonymized,
    DateTimeOffset CreatedAt,
    GuestStatsDto Stats);

/// <summary>Текущее состояние согласия.</summary>
public sealed record ConsentStateDto(string Type, bool Granted, string Source, DateTimeOffset RecordedAt);

public sealed record GuestDto(
    Guid Id,
    string FullName,
    string? Phone,
    string? WhatsAppPhone,
    string? Email,
    DateOnly? BirthDate,
    string? Notes,
    string Source,
    bool HasAccount,
    bool IsAnonymized,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    GuestStatsDto Stats,
    IReadOnlyList<ConsentStateDto> Consents,
    uint Version);

/// <summary>Для выбора гостя при брони.</summary>
public sealed record GuestLookupDto(Guid Id, string FullName, string? Phone, string? Email, int BookingsCount);

public sealed record SaveGuestRequest(
    string FullName,
    string? Phone,
    string? WhatsAppPhone,
    string? Email,
    DateOnly? BirthDate,
    string? Notes,
    uint? Version);

/// <param name="Type">PersonalData или WhatsApp.</param>
/// <param name="Source">Portal, FrontDesk, Phone, WhatsApp, Written — откуда получено согласие или отзыв.</param>
public sealed record SetConsentRequest(string Type, bool Granted, string Source);

public sealed record ConsentHistoryDto(Guid Id, string Type, bool Granted, string Source, string? RecordedByName, DateTimeOffset RecordedAt);

public sealed record GuestBookingDto(Guid BookingId, string Number, string Status, string Kind, string StartsAt, string EndsAt, string RoomNumber, decimal TotalAmount, DateTimeOffset CreatedAt);

/// <param name="Filter">withBookings — с бронями; whatsApp — с согласием на WhatsApp; noContact — без телефона и email.</param>
public sealed record GuestQuery(string? Search, string? Filter, int? Page, int? PageSize);

public interface IGuestService
{
    Task<PagedResult<GuestListItemDto>> ListAsync(GuestQuery query, CancellationToken ct);

    Task<IReadOnlyList<GuestLookupDto>> LookupAsync(string? query, CancellationToken ct);

    Task<Result<GuestDto>> GetAsync(Guid id, CancellationToken ct);

    Task<Result<GuestDto>> CreateAsync(SaveGuestRequest request, CancellationToken ct);

    Task<Result<GuestDto>> UpdateAsync(Guid id, SaveGuestRequest request, CancellationToken ct);

    Task<Result<GuestDto>> SetConsentAsync(Guid id, SetConsentRequest request, CancellationToken ct);

    Task<Result<IReadOnlyList<ConsentHistoryDto>>> GetConsentHistoryAsync(Guid id, CancellationToken ct);

    Task<Result<IReadOnlyList<GuestBookingDto>>> GetBookingsAsync(Guid id, CancellationToken ct);

    Task<Result<GuestDto>> AnonymizeAsync(Guid id, CancellationToken ct);
}

public static class GuestAppErrors
{
    public static Error Duplicate(Guid existingId, string by) =>
        Error.Conflict("guest.duplicate", $"Гость с таким {by} уже есть в базе.") with { Details = new Dictionary<string, object?> { ["guestId"] = existingId } };

    public static readonly Error ConcurrencyConflict = Error.Conflict("concurrency.conflict", "Карточку гостя изменил другой пользователь. Обновите страницу и повторите.");
}

public sealed class SaveGuestRequestValidator : AbstractValidator<SaveGuestRequest>
{
    public SaveGuestRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().WithMessage("Укажите имя гостя.").MaximumLength(200);
        RuleFor(x => x.Phone).Must(v => PhoneNumber.Parse(v).IsSuccess).When(x => !string.IsNullOrWhiteSpace(x.Phone))
            .WithMessage("Некорректный номер телефона. Пример: +996 XXX XXX XXX.");
        RuleFor(x => x.WhatsAppPhone).Must(v => PhoneNumber.Parse(v).IsSuccess).When(x => !string.IsNullOrWhiteSpace(x.WhatsAppPhone))
            .WithMessage("Некорректный номер WhatsApp. Пример: +996 XXX XXX XXX.");
        RuleFor(x => x.Email).EmailAddress().WithMessage("Некорректный email.").MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x).Must(x => !string.IsNullOrWhiteSpace(x.Phone) || !string.IsNullOrWhiteSpace(x.Email))
            .OverridePropertyName("phone").WithMessage("Укажите телефон или email гостя.");
        RuleFor(x => x.BirthDate).Must(d => d is null || (d.Value.Year >= 1900 && d.Value <= DateOnly.FromDateTime(DateTime.UtcNow)))
            .WithMessage("Некорректная дата рождения.");
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public sealed class SetConsentRequestValidator : AbstractValidator<SetConsentRequest>
{
    public SetConsentRequestValidator()
    {
        RuleFor(x => x.Type).Must(t => Enum.TryParse<Contracts.ConsentType>(t, out _)).WithMessage("Неизвестный вид согласия.");
        RuleFor(x => x.Source).Must(s => Enum.TryParse<ConsentSource>(s, out _)).WithMessage("Укажите, откуда получено согласие.");
    }
}
