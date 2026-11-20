using Hms.Modules.Rooms.Contracts;
using Hms.SharedKernel;

namespace Hms.Modules.Rooms.Domain;

#pragma warning disable CA1720 // Single/Double — гостиничные термины (тип кровати), а не имена типов
public enum BedType
{
    Single,
    Double,
    Twin,
    Queen,
    King,
    Bunk,
}
#pragma warning restore CA1720

/// <summary>Удобство (Wi-Fi, кондиционер…) — справочник гостиницы.</summary>
public sealed class Amenity
{
    private Amenity()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public Amenity(Guid hotelId, string code, string name, string? icon, int sortOrder)
        : this()
    {
        Id = Guid.CreateVersion7();
        HotelId = hotelId;
        Code = code;
        Name = name;
        Icon = icon;
        SortOrder = sortOrder;
    }

    public Guid Id { get; private set; }

    public Guid HotelId { get; private set; }

    /// <summary>Машинный код (wifi, ac…), неизменяем.</summary>
    public string Code { get; private set; }

    public string Name { get; private set; }

    /// <summary>Имя иконки Lucide (wifi, snowflake…).</summary>
    public string? Icon { get; private set; }

    public int SortOrder { get; private set; }

    public void Update(string name, string? icon, int sortOrder)
    {
        Name = name.Trim();
        Icon = string.IsNullOrWhiteSpace(icon) ? null : icon.Trim();
        SortOrder = sortOrder;
    }
}

/// <summary>Категория номеров: вместимость, кровати, базовая цена, удобства, фото (ROOM-2, RATE-1).</summary>
public sealed class RoomType
{
    private readonly List<RoomTypeAmenity> _amenities = [];

    private RoomType()
    {
        Name = string.Empty;
    }

    public RoomType(Guid hotelId, string name, DateTimeOffset now)
        : this()
    {
        Id = Guid.CreateVersion7();
        HotelId = hotelId;
        Name = name;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid HotelId { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    /// <summary>Максимум гостей (взрослые + дети).</summary>
    public int Capacity { get; private set; }

    public int BedsCount { get; private set; }

    public BedType BedType { get; private set; }

    /// <summary>Базовая цена за ночь, KGS (numeric(14,2)).</summary>
    public decimal BasePrice { get; private set; }

    public int SortOrder { get; private set; }

    public bool IsArchived { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public IReadOnlyList<RoomTypeAmenity> Amenities => _amenities;

    public void Update(string name, string? description, int capacity, int bedsCount, BedType bedType, Money basePrice, int sortOrder, DateTimeOffset now)
    {
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Capacity = capacity;
        BedsCount = bedsCount;
        BedType = bedType;
        BasePrice = basePrice.Amount;
        SortOrder = sortOrder;
        UpdatedAt = now;
    }

    public void SetAmenities(IEnumerable<Guid> amenityIds, DateTimeOffset now)
    {
        var target = amenityIds.ToHashSet();
        _amenities.RemoveAll(a => !target.Contains(a.AmenityId));
        foreach (var id in target.Where(id => _amenities.All(a => a.AmenityId != id)))
        {
            _amenities.Add(new RoomTypeAmenity(Id, id));
        }

        UpdatedAt = now;
    }

    public void Archive(DateTimeOffset now)
    {
        IsArchived = true;
        UpdatedAt = now;
    }

    public void Restore(DateTimeOffset now)
    {
        IsArchived = false;
        UpdatedAt = now;
    }
}

public sealed class RoomTypeAmenity
{
    private RoomTypeAmenity()
    {
    }

    public RoomTypeAmenity(Guid roomTypeId, Guid amenityId)
    {
        RoomTypeId = roomTypeId;
        AmenityId = amenityId;
    }

    public Guid RoomTypeId { get; private set; }

    public Guid AmenityId { get; private set; }
}

/// <summary>Номер. Характеристики, не заданные у номера, берутся из категории.</summary>
public sealed class Room
{
    public static readonly Error ArchiveNotAllowed = Error.BusinessRule(
        "room.archive_not_allowed", "Нельзя архивировать занятый номер или номер на уборке.");

    private Room()
    {
        Number = string.Empty;
    }

    public Room(Guid hotelId, Guid roomTypeId, string number, DateTimeOffset now)
        : this()
    {
        Id = Guid.CreateVersion7();
        HotelId = hotelId;
        RoomTypeId = roomTypeId;
        Number = number;
        Status = RoomStatus.Available;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid HotelId { get; private set; }

    public Guid RoomTypeId { get; private set; }

    public string Number { get; private set; }

    public int? Floor { get; private set; }

    public int? Capacity { get; private set; }

    public int? BedsCount { get; private set; }

    public BedType? BedType { get; private set; }

    /// <summary>Цена за ночь этого номера вместо базовой цены категории (RATE-1).</summary>
    public decimal? PriceOverride { get; private set; }

    public string? Description { get; private set; }

    public RoomStatus Status { get; private set; }

    public bool IsArchived { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public void Update(Guid roomTypeId, string number, int? floor, int? capacity, int? bedsCount, BedType? bedType, Money? priceOverride, string? description, DateTimeOffset now)
    {
        RoomTypeId = roomTypeId;
        Number = number.Trim();
        Floor = floor;
        Capacity = capacity;
        BedsCount = bedsCount;
        BedType = bedType;
        PriceOverride = priceOverride?.Amount;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        UpdatedAt = now;
    }

    /// <summary>Меняет статус по правилам <see cref="RoomStatusMachine"/>; возвращает запись истории.</summary>
    public Result<RoomStatusChange> ChangeStatus(RoomStatus target, RoomStatusSource source, string? reason, Guid? changedBy, Guid? bookingId, DateTimeOffset now)
    {
        if (IsArchived)
        {
            return Error.BusinessRule("room.archived", "Номер в архиве.");
        }

        if (!RoomStatusMachine.CanTransition(Status, target, source))
        {
            return Error.Conflict("room.invalid_status_transition", $"Нельзя перевести номер из статуса «{Status}» в «{target}».");
        }

        if (source == RoomStatusSource.Manual && string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("room.reason_required", "Укажите причину смены статуса.");
        }

        var change = new RoomStatusChange(Id, Status, target, source, reason?.Trim(), changedBy, bookingId, now);
        Status = target;
        UpdatedAt = now;
        return change;
    }

    public Result Archive(DateTimeOffset now)
    {
        if (Status is RoomStatus.Occupied or RoomStatus.Cleaning)
        {
            return ArchiveNotAllowed;
        }

        IsArchived = true;
        UpdatedAt = now;
        return Result.Success();
    }

    public void Restore(DateTimeOffset now)
    {
        IsArchived = false;
        UpdatedAt = now;
    }
}

/// <summary>Запись истории статусов номера (ROOM-3): кто, когда, откуда, куда и почему.</summary>
public sealed class RoomStatusChange
{
    private RoomStatusChange()
    {
    }

    public RoomStatusChange(Guid roomId, RoomStatus from, RoomStatus to, RoomStatusSource source, string? reason, Guid? changedBy, Guid? bookingId, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        RoomId = roomId;
        FromStatus = from;
        ToStatus = to;
        Source = source;
        Reason = reason;
        ChangedBy = changedBy;
        BookingId = bookingId;
        ChangedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid RoomId { get; private set; }

    public RoomStatus FromStatus { get; private set; }

    public RoomStatus ToStatus { get; private set; }

    public RoomStatusSource Source { get; private set; }

    public string? Reason { get; private set; }

    public Guid? ChangedBy { get; private set; }

    public Guid? BookingId { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }
}

/// <summary>Фото категории или конкретного номера (ровно одно из двух).</summary>
public sealed class RoomPhoto
{
    private RoomPhoto()
    {
    }

    private RoomPhoto(Guid? roomTypeId, Guid? roomId, Guid fileId, int sortOrder)
    {
        Id = Guid.CreateVersion7();
        RoomTypeId = roomTypeId;
        RoomId = roomId;
        FileId = fileId;
        SortOrder = sortOrder;
    }

    public Guid Id { get; private set; }

    public Guid? RoomTypeId { get; private set; }

    public Guid? RoomId { get; private set; }

    public Guid FileId { get; private set; }

    public int SortOrder { get; private set; }

    public static RoomPhoto ForRoomType(Guid roomTypeId, Guid fileId, int sortOrder) => new(roomTypeId, null, fileId, sortOrder);

    public static RoomPhoto ForRoom(Guid roomId, Guid fileId, int sortOrder) => new(null, roomId, fileId, sortOrder);

    public void MoveTo(int sortOrder) => SortOrder = sortOrder;
}
