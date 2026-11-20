using FluentValidation;
using Hms.SharedKernel;

namespace Hms.Modules.Rooms.Application;

// ---- DTO ----

public sealed record AmenityDto(Guid Id, string Code, string Name, string? Icon, int SortOrder);

public sealed record CreateAmenityRequest(string Code, string Name, string? Icon, int SortOrder);

public sealed record UpdateAmenityRequest(string Name, string? Icon, int SortOrder);

/// <param name="Url">Относительный адрес файла в API (<c>/api/v1/files/{id}</c>).</param>
public sealed record PhotoDto(Guid Id, Guid FileId, string Url, int SortOrder);

public sealed record RoomTypeDto(
    Guid Id,
    string Name,
    string? Description,
    int Capacity,
    int BedsCount,
    string BedType,
    decimal BasePrice,
    int SortOrder,
    bool IsArchived,
    int ActiveRoomCount,
    IReadOnlyList<AmenityDto> Amenities,
    IReadOnlyList<PhotoDto> Photos,
    uint Version,
    decimal? HourlyPrice = null);

public sealed record SaveRoomTypeRequest(
    string Name,
    string? Description,
    int Capacity,
    int BedsCount,
    string BedType,
    decimal BasePrice,
    int SortOrder,
    uint? Version,
    decimal? HourlyPrice = null);

public sealed record SetAmenitiesRequest(IReadOnlyList<Guid> AmenityIds);

public sealed record ReorderPhotosRequest(IReadOnlyList<Guid> PhotoIds);

/// <summary>Номер: собственные значения (null — как у категории) и итоговые (Effective*).</summary>
public sealed record RoomDto(
    Guid Id,
    string Number,
    int? Floor,
    Guid RoomTypeId,
    string RoomTypeName,
    int? Capacity,
    int? BedsCount,
    string? BedType,
    decimal? PriceOverride,
    int EffectiveCapacity,
    int EffectiveBedsCount,
    string EffectiveBedType,
    decimal EffectivePrice,
    string? Description,
    string Status,
    string DisplayStatus,
    IReadOnlyList<string> ManualStatusTargets,
    bool IsArchived,
    IReadOnlyList<PhotoDto> Photos,
    uint Version,
    decimal? HourlyPriceOverride = null,
    decimal? EffectiveHourlyPrice = null,
    string? OccupiedUntil = null,
    string? NextBookingAt = null);

public sealed record SaveRoomRequest(
    Guid RoomTypeId,
    string Number,
    int? Floor,
    int? Capacity,
    int? BedsCount,
    string? BedType,
    decimal? PriceOverride,
    string? Description,
    uint? Version,
    decimal? HourlyPriceOverride = null);

public sealed record ChangeRoomStatusRequest(string Status, string? Reason);

public sealed record RoomStatusHistoryDto(
    Guid Id,
    string FromStatus,
    string ToStatus,
    string Source,
    string? Reason,
    Guid? ChangedBy,
    string? ChangedByName,
    DateTimeOffset ChangedAt);

public sealed record RoomQuery(Guid? RoomTypeId, string? Status, int? Floor, bool? IncludeArchived, string? Search);

/// <summary>Категория в публичном каталоге: без служебных полей; цена «от» — минимальная по номерам категории.</summary>
public sealed record PublicRoomTypeDto(
    Guid Id,
    string Name,
    string? Description,
    int Capacity,
    int BedsCount,
    string BedType,
    decimal PriceFrom,
    IReadOnlyList<AmenityDto> Amenities,
    IReadOnlyList<PhotoDto> Photos,
    decimal? HourlyPriceFrom = null);

// ---- Сервисы ----

public interface IAmenityService
{
    Task<IReadOnlyList<AmenityDto>> ListAsync(CancellationToken ct);

    Task<Result<AmenityDto>> CreateAsync(CreateAmenityRequest request, CancellationToken ct);

    Task<Result<AmenityDto>> UpdateAsync(Guid id, UpdateAmenityRequest request, CancellationToken ct);

    Task<Result> DeleteAsync(Guid id, CancellationToken ct);
}

public interface IRoomTypeService
{
    Task<IReadOnlyList<RoomTypeDto>> ListAsync(bool includeArchived, CancellationToken ct);

    Task<Result<RoomTypeDto>> GetAsync(Guid id, CancellationToken ct);

    Task<Result<RoomTypeDto>> CreateAsync(SaveRoomTypeRequest request, CancellationToken ct);

    Task<Result<RoomTypeDto>> UpdateAsync(Guid id, SaveRoomTypeRequest request, CancellationToken ct);

    Task<Result<RoomTypeDto>> SetArchivedAsync(Guid id, bool archived, CancellationToken ct);

    Task<Result<RoomTypeDto>> SetAmenitiesAsync(Guid id, SetAmenitiesRequest request, CancellationToken ct);

    Task<Result<PhotoDto>> AddPhotoAsync(Guid id, Stream content, string fileName, CancellationToken ct);

    Task<Result> DeletePhotoAsync(Guid id, Guid photoId, CancellationToken ct);

    Task<Result<RoomTypeDto>> ReorderPhotosAsync(Guid id, ReorderPhotosRequest request, CancellationToken ct);
}

public interface IRoomService
{
    Task<IReadOnlyList<RoomDto>> ListAsync(RoomQuery query, CancellationToken ct);

    Task<Result<RoomDto>> GetAsync(Guid id, CancellationToken ct);

    Task<Result<RoomDto>> CreateAsync(SaveRoomRequest request, CancellationToken ct);

    Task<Result<RoomDto>> UpdateAsync(Guid id, SaveRoomRequest request, CancellationToken ct);

    Task<Result> DeleteAsync(Guid id, CancellationToken ct);

    Task<Result<RoomDto>> SetArchivedAsync(Guid id, bool archived, CancellationToken ct);

    Task<Result<RoomDto>> ChangeStatusAsync(Guid id, ChangeRoomStatusRequest request, CancellationToken ct);

    Task<Result<IReadOnlyList<RoomStatusHistoryDto>>> GetStatusHistoryAsync(Guid id, CancellationToken ct);

    Task<Result<PhotoDto>> AddPhotoAsync(Guid id, Stream content, string fileName, CancellationToken ct);

    Task<Result> DeletePhotoAsync(Guid id, Guid photoId, CancellationToken ct);
}

public interface IRoomCatalog
{
    Task<IReadOnlyList<PublicRoomTypeDto>> ListAsync(CancellationToken ct);

    Task<Result<PublicRoomTypeDto>> GetAsync(Guid id, CancellationToken ct);
}

public static class RoomErrors
{
    public static readonly Error RoomNotFound = Error.NotFound("room.not_found", "Номер не найден.");
    public static readonly Error RoomTypeNotFound = Error.NotFound("room_type.not_found", "Категория не найдена.");
    public static readonly Error RoomTypeArchived = Error.BusinessRule("room_type.archived", "Категория в архиве — выберите действующую.");
    public static readonly Error RoomTypeHasActiveRooms = Error.Conflict("room_type.has_active_rooms", "В категории есть действующие номера — сначала перенесите или архивируйте их.");
    public static readonly Error NumberTaken = Error.Conflict("room.number_taken", "Номер с таким обозначением уже есть.");
    public static readonly Error HasHistory = Error.Conflict("room.has_history", "У номера есть история — его можно только архивировать.");
    public static readonly Error AmenityNotFound = Error.NotFound("amenity.not_found", "Удобство не найдено.");
    public static readonly Error AmenityCodeTaken = Error.Conflict("amenity.code_taken", "Удобство с таким кодом уже есть.");
    public static readonly Error AmenityInUse = Error.Conflict("amenity.in_use", "Удобство указано у категорий — сначала уберите его оттуда.");
    public static readonly Error UnknownAmenity = Error.Validation("amenity.unknown", "Среди выбранных есть несуществующее удобство.");
    public static readonly Error PhotoNotFound = Error.NotFound("photo.not_found", "Фото не найдено.");
    public static readonly Error TooManyPhotos = Error.BusinessRule("photo.too_many", "Не более 20 фото.");
    public static readonly Error PhotoOrderMismatch = Error.Validation("photo.order_mismatch", "Список фото не совпадает с текущим.");
    public static readonly Error ConcurrencyConflict = Error.Conflict("concurrency.conflict", "Данные изменил другой пользователь. Обновите страницу и повторите.");
    public static readonly Error UnknownStatus = Error.Validation("room.unknown_status", "Неизвестный статус номера.");
    public static readonly Error HasBookings = Error.Conflict("room.has_bookings", "У номера есть брони — его можно только архивировать.");
    public static readonly Error HasUpcomingBookings = Error.Conflict("room.has_upcoming_bookings", "На номер есть действующие брони на будущие даты — сначала перенесите или отмените их.");
}

// ---- Валидация ----

internal static class RoomRules
{
    public static readonly string[] BedTypes = ["Single", "Double", "Twin", "Queen", "King", "Bunk"];

    public static bool IsMoney(decimal value) =>
        value > 0 && value <= Money.MaxAbsoluteAmount && decimal.Round(value, 2) == value;
}

public sealed class SaveRoomTypeRequestValidator : AbstractValidator<SaveRoomTypeRequest>
{
    public SaveRoomTypeRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Укажите название категории.").MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Capacity).InclusiveBetween(1, 20).WithMessage("Вместимость — от 1 до 20 гостей.");
        RuleFor(x => x.BedsCount).InclusiveBetween(1, 20).WithMessage("Кроватей — от 1 до 20.");
        RuleFor(x => x.BedType).Must(RoomRules.BedTypes.Contains).WithMessage("Выберите тип кроватей.");
        RuleFor(x => x.BasePrice).Must(RoomRules.IsMoney).WithMessage("Цена — больше нуля, не более двух знаков после запятой.");
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 1000);
        RuleFor(x => x.HourlyPrice).Must(v => RoomRules.IsMoney(v!.Value)).When(x => x.HourlyPrice is not null)
            .WithMessage("Цена часа — больше нуля, не более двух знаков после запятой.");
    }
}

public sealed class SaveRoomRequestValidator : AbstractValidator<SaveRoomRequest>
{
    public SaveRoomRequestValidator()
    {
        RuleFor(x => x.RoomTypeId).NotEmpty().WithMessage("Выберите категорию.");
        RuleFor(x => x.Number).NotEmpty().WithMessage("Укажите номер комнаты.")
            .MaximumLength(10).Matches("^[0-9A-Za-zА-Яа-я-]+$").WithMessage("Номер: цифры, буквы и дефис, до 10 символов.");
        RuleFor(x => x.Floor).InclusiveBetween(-5, 200).When(x => x.Floor is not null).WithMessage("Этаж — от -5 до 200.");
        RuleFor(x => x.Capacity).InclusiveBetween(1, 20).When(x => x.Capacity is not null).WithMessage("Вместимость — от 1 до 20 гостей.");
        RuleFor(x => x.BedsCount).InclusiveBetween(1, 20).When(x => x.BedsCount is not null).WithMessage("Кроватей — от 1 до 20.");
        RuleFor(x => x.BedType).Must(v => RoomRules.BedTypes.Contains(v)).When(x => x.BedType is not null).WithMessage("Выберите тип кроватей.");
        RuleFor(x => x.PriceOverride).Must(v => RoomRules.IsMoney(v!.Value)).When(x => x.PriceOverride is not null)
            .WithMessage("Цена — больше нуля, не более двух знаков после запятой.");
        RuleFor(x => x.HourlyPriceOverride).Must(v => RoomRules.IsMoney(v!.Value)).When(x => x.HourlyPriceOverride is not null)
            .WithMessage("Цена часа — больше нуля, не более двух знаков после запятой.");
        RuleFor(x => x.Description).MaximumLength(2000);
    }
}

public sealed class ChangeRoomStatusRequestValidator : AbstractValidator<ChangeRoomStatusRequest>
{
    public ChangeRoomStatusRequestValidator()
    {
        RuleFor(x => x.Status).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Укажите причину смены статуса.").MaximumLength(500);
    }
}

public sealed class CreateAmenityRequestValidator : AbstractValidator<CreateAmenityRequest>
{
    public CreateAmenityRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50).Matches("^[a-z0-9_-]+$").WithMessage("Код: латиница в нижнем регистре, цифры, «_» и «-».");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Укажите название.").MaximumLength(100);
        RuleFor(x => x.Icon).MaximumLength(50);
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 1000);
    }
}

public sealed class UpdateAmenityRequestValidator : AbstractValidator<UpdateAmenityRequest>
{
    public UpdateAmenityRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Укажите название.").MaximumLength(100);
        RuleFor(x => x.Icon).MaximumLength(50);
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 1000);
    }
}

public sealed class SetAmenitiesRequestValidator : AbstractValidator<SetAmenitiesRequest>
{
    public SetAmenitiesRequestValidator() => RuleFor(x => x.AmenityIds).NotNull().Must(l => l.Count <= 100);
}

public sealed class ReorderPhotosRequestValidator : AbstractValidator<ReorderPhotosRequest>
{
    public ReorderPhotosRequestValidator() => RuleFor(x => x.PhotoIds).NotNull().Must(l => l.Count <= 20 && l.Distinct().Count() == l.Count);
}
