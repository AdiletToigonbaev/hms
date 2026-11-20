using Hms.Modules.Audit.Contracts;
using Hms.Modules.Bookings.Contracts;
using Hms.Modules.Hotel.Contracts;
using Hms.Modules.Identity.Contracts;
using Hms.Modules.Rooms.Application;
using Hms.Modules.Rooms.Contracts;
using Hms.Modules.Rooms.Domain;
using Hms.Platform.Security;
using Hms.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Hms.Modules.Rooms.Infrastructure;

internal sealed class RoomService(
    DbContext db,
    IHotelContext hotel,
    IAuditTrail audit,
    ICurrentUser currentUser,
    IUserDirectory users,
    RoomsStore store,
    IRoomBookingQueries bookings,
    Hms.Platform.Time.IHotelClock clock,
    TimeProvider time) : IRoomService
{
    public async Task<IReadOnlyList<RoomDto>> ListAsync(RoomQuery q, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var query = db.Set<Room>().AsNoTracking().Where(r => r.HotelId == hotelId);
        if (q.IncludeArchived != true)
        {
            query = query.Where(r => !r.IsArchived);
        }

        if (q.RoomTypeId is { } typeId)
        {
            query = query.Where(r => r.RoomTypeId == typeId);
        }

        if (!string.IsNullOrWhiteSpace(q.Status) && Enum.TryParse<RoomStatus>(q.Status, out var status))
        {
            query = query.Where(r => r.Status == status);
        }

        if (q.Floor is { } floor)
        {
            query = query.Where(r => r.Floor == floor);
        }

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var search = q.Search.Trim();
            query = query.Where(r => r.Number.Contains(search));
        }

        var rooms = await query.OrderBy(r => r.Floor).ThenBy(r => r.Number).ToListAsync(ct);
        var types = await TypesAsync(rooms.Select(r => r.RoomTypeId), ct);
        var today = await TodayAsync(ct);
        return rooms.Select(r => ToDto(r, types[r.RoomTypeId], [], today.GetValueOrDefault(r.Id))).ToList();
    }

    public async Task<Result<RoomDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var room = await FindAsync(id, tracking: false, ct);
        return room is null ? RoomErrors.RoomNotFound : await ToDtoAsync(room, ct);
    }

    public async Task<Result<RoomDto>> CreateAsync(SaveRoomRequest r, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        if (await ValidateTypeAsync(r.RoomTypeId, hotelId, ct) is { } typeError)
        {
            return typeError;
        }

        var number = r.Number.Trim();
        if (await db.Set<Room>().AnyAsync(x => x.HotelId == hotelId && x.Number == number, ct))
        {
            return RoomErrors.NumberTaken;
        }

        var now = time.GetUtcNow();
        var room = new Room(hotelId, r.RoomTypeId, number, now);
        Apply(room, r, now);
        db.Set<Room>().Add(room);
        audit.Record(new AuditEntry("room.created", "Room", room.Id.ToString(), After: Snapshot(room)));
        return await store.SaveAsync(ct) is { } error ? error : await ToDtoAsync(room, ct);
    }

    public async Task<Result<RoomDto>> UpdateAsync(Guid id, SaveRoomRequest r, CancellationToken ct)
    {
        var room = await FindAsync(id, tracking: true, ct);
        if (room is null)
        {
            return RoomErrors.RoomNotFound;
        }

        if (r.RoomTypeId != room.RoomTypeId && await ValidateTypeAsync(r.RoomTypeId, room.HotelId, ct) is { } typeError)
        {
            return typeError;
        }

        var number = r.Number.Trim();
        if (await db.Set<Room>().AnyAsync(x => x.HotelId == room.HotelId && x.Number == number && x.Id != id, ct))
        {
            return RoomErrors.NumberTaken;
        }

        if (r.Version is { } version)
        {
            db.Entry(room).Property(x => x.Version).OriginalValue = version;
        }

        var before = Snapshot(room);
        Apply(room, r, time.GetUtcNow());
        audit.Record(new AuditEntry("room.updated", "Room", id.ToString(), before, Snapshot(room)));
        return await store.SaveAsync(ct) is { } error ? error : await ToDtoAsync(room, ct);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct)
    {
        var room = await FindAsync(id, tracking: true, ct);
        if (room is null)
        {
            return RoomErrors.RoomNotFound;
        }

        // ROOM-1: номер с историей (брони, смены статусов) только архивируется.
        if (await bookings.HasAnyBookingAsync(id, ct))
        {
            return RoomErrors.HasBookings;
        }

        if (await db.Set<RoomStatusChange>().AnyAsync(h => h.RoomId == id, ct))
        {
            return RoomErrors.HasHistory;
        }

        foreach (var photo in await db.Set<RoomPhoto>().Where(p => p.RoomId == id).Select(p => p.Id).ToListAsync(ct))
        {
            await store.DeletePhotoAsync(null, id, photo, ct);
        }

        db.Set<Room>().Remove(room);
        audit.Record(new AuditEntry("room.deleted", "Room", id.ToString(), Before: Snapshot(room)));
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<RoomDto>> SetArchivedAsync(Guid id, bool archived, CancellationToken ct)
    {
        var room = await FindAsync(id, tracking: true, ct);
        if (room is null)
        {
            return RoomErrors.RoomNotFound;
        }

        var now = time.GetUtcNow();
        if (archived)
        {
            if (await bookings.HasUpcomingBookingsAsync(id, clock.Today, ct))
            {
                return RoomErrors.HasUpcomingBookings;
            }

            var result = room.Archive(now);
            if (result.IsFailure)
            {
                return result.Error;
            }
        }
        else
        {
            if (await ValidateTypeAsync(room.RoomTypeId, room.HotelId, ct) is { } typeError)
            {
                return typeError;
            }

            room.Restore(now);
        }

        audit.Record(new AuditEntry(archived ? "room.archived" : "room.restored", "Room", id.ToString()));
        return await store.SaveAsync(ct) is { } error ? error : await ToDtoAsync(room, ct);
    }

    public async Task<Result<RoomDto>> ChangeStatusAsync(Guid id, ChangeRoomStatusRequest r, CancellationToken ct)
    {
        if (!Enum.TryParse<RoomStatus>(r.Status, out var target) || !Enum.IsDefined(target))
        {
            return RoomErrors.UnknownStatus;
        }

        var room = await FindAsync(id, tracking: true, ct);
        if (room is null)
        {
            return RoomErrors.RoomNotFound;
        }

        if (target == RoomStatus.OutOfService && await bookings.HasUpcomingBookingsAsync(id, clock.Today, ct))
        {
            return RoomErrors.HasUpcomingBookings;
        }

        var from = room.Status;
        var change = room.ChangeStatus(target, RoomStatusSource.Manual, r.Reason, currentUser.UserId, null, time.GetUtcNow());
        if (change.IsFailure)
        {
            return change.Error;
        }

        db.Set<RoomStatusChange>().Add(change.Value);
        audit.Record(new AuditEntry("room.status_changed", "Room", id.ToString(),
            new { Status = from.ToString() }, new { Status = target.ToString(), r.Reason }));
        return await store.SaveAsync(ct) is { } error ? error : await ToDtoAsync(room, ct);
    }

    public async Task<Result<IReadOnlyList<RoomStatusHistoryDto>>> GetStatusHistoryAsync(Guid id, CancellationToken ct)
    {
        if (await FindAsync(id, tracking: false, ct) is null)
        {
            return RoomErrors.RoomNotFound;
        }

        var history = await db.Set<RoomStatusChange>().AsNoTracking()
            .Where(h => h.RoomId == id)
            .OrderByDescending(h => h.ChangedAt)
            .Take(200)
            .ToListAsync(ct);
        var names = await users.GetNamesAsync(history.Where(h => h.ChangedBy != null).Select(h => h.ChangedBy!.Value), ct);
        return history.Select(h => new RoomStatusHistoryDto(
                h.Id, h.FromStatus.ToString(), h.ToStatus.ToString(), h.Source.ToString(), h.Reason, h.ChangedBy,
                h.ChangedBy is { } by && names.TryGetValue(by, out var name) ? name : null, h.ChangedAt))
            .ToList();
    }

    public async Task<Result<PhotoDto>> AddPhotoAsync(Guid id, Stream content, string fileName, CancellationToken ct)
    {
        if (await FindAsync(id, tracking: false, ct) is null)
        {
            return RoomErrors.RoomNotFound;
        }

        return await store.AddPhotoAsync(null, id, content, fileName, ct);
    }

    public async Task<Result> DeletePhotoAsync(Guid id, Guid photoId, CancellationToken ct)
    {
        if (await FindAsync(id, tracking: false, ct) is null)
        {
            return RoomErrors.RoomNotFound;
        }

        return await store.DeletePhotoAsync(null, id, photoId, ct);
    }

    private static void Apply(Room room, SaveRoomRequest r, DateTimeOffset now)
    {
        room.Update(
        r.RoomTypeId,
        r.Number,
        r.Floor,
        r.Capacity,
        r.BedsCount,
        r.BedType is null ? null : Enum.Parse<BedType>(r.BedType),
        r.PriceOverride is { } price ? Money.Kgs(price) : null,
        r.Description,
        now);
        room.SetHourlyPriceOverride(r.HourlyPriceOverride is { } hourly ? Money.Kgs(hourly) : null);
    }

    private async Task<Error?> ValidateTypeAsync(Guid typeId, Guid hotelId, CancellationToken ct)
    {
        var type = await db.Set<RoomType>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == typeId && t.HotelId == hotelId, ct);
        return type is null ? RoomErrors.RoomTypeNotFound : type.IsArchived ? RoomErrors.RoomTypeArchived : null;
    }

    private async Task<Room?> FindAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var query = db.Set<Room>().Where(r => r.Id == id && r.HotelId == hotelId);
        return await (tracking ? query : query.AsNoTracking()).FirstOrDefaultAsync(ct);
    }

    private async Task<Dictionary<Guid, RoomType>> TypesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Set<RoomType>().AsNoTracking().Where(t => list.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
    }

    private async Task<RoomDto> ToDtoAsync(Room room, CancellationToken ct)
    {
        var type = await db.Set<RoomType>().AsNoTracking().FirstAsync(t => t.Id == room.RoomTypeId, ct);
        var today = await TodayAsync(ct);
        return ToDto(room, type, await store.PhotosAsync(null, room.Id, ct), today.GetValueOrDefault(room.Id));
    }

    /// <summary>Занятость номеров на сегодня по броням — для статуса «Бронь» и подсказки «в 15:00 / до 14:30».</summary>
    private Task<IReadOnlyDictionary<Guid, RoomTodayState>> TodayAsync(CancellationToken ct) =>
        bookings.TodayStatesAsync(clock.UtcNow, clock.ToUtc(clock.Today.AddDays(1), TimeOnly.MinValue), ct);

    private string? Local(DateTimeOffset? utc) =>
        utc is { } value ? clock.ToHotelTime(value).ToString("yyyy-MM-ddTHH:mm", System.Globalization.CultureInfo.InvariantCulture) : null;

    /// <param name="today">Брони номера на сегодня: свободный номер с бронью показывается как «Бронь» (в 15:00 / до 14:30).</param>
    private RoomDto ToDto(Room r, RoomType t, IReadOnlyList<PhotoDto> photos, RoomTodayState? today) => new(
        r.Id,
        r.Number,
        r.Floor,
        r.RoomTypeId,
        t.Name,
        r.Capacity,
        r.BedsCount,
        r.BedType?.ToString(),
        r.PriceOverride,
        r.Capacity ?? t.Capacity,
        r.BedsCount ?? t.BedsCount,
        (r.BedType ?? t.BedType).ToString(),
        r.PriceOverride ?? t.BasePrice,
        r.Description,
        r.Status.ToString(),
        r.Status == RoomStatus.Available && today is not null ? "Reserved" : r.Status.ToString(),
        r.IsArchived ? [] : RoomStatusMachine.ManualTargets(r.Status).Select(s => s.ToString()).ToList(),
        r.IsArchived,
        photos,
        r.Version,
        r.HourlyPriceOverride,
        r.HourlyPriceOverride ?? t.HourlyPrice,
        Local(today?.OccupiedUntil),
        Local(today?.NextStartsAt));

    private static object Snapshot(Room r) => new { r.Number, r.Floor, r.RoomTypeId, r.Capacity, r.BedsCount, BedType = r.BedType?.ToString(), r.PriceOverride, r.HourlyPriceOverride };
}

internal sealed class RoomCatalog(DbContext db, IHotelContext hotel, RoomsStore store) : IRoomCatalog
{
    public async Task<IReadOnlyList<PublicRoomTypeDto>> ListAsync(CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var types = await db.Set<RoomType>().AsNoTracking().Include(t => t.Amenities)
            .Where(t => t.HotelId == hotelId && !t.IsArchived)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .ToListAsync(ct);
        var result = new List<PublicRoomTypeDto>();
        foreach (var type in types)
        {
            if (await ToDtoAsync(type, ct) is { } dto)
            {
                result.Add(dto);
            }
        }

        return result;
    }

    public async Task<Result<PublicRoomTypeDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var type = await db.Set<RoomType>().AsNoTracking().Include(t => t.Amenities)
            .FirstOrDefaultAsync(t => t.Id == id && t.HotelId == hotelId && !t.IsArchived, ct);
        return type is not null && await ToDtoAsync(type, ct) is { } dto ? dto : RoomErrors.RoomTypeNotFound;
    }

    /// <summary>Категория показывается, если в ней есть хотя бы один номер в продаже (не в архиве и не «вне продажи»).</summary>
    private async Task<PublicRoomTypeDto?> ToDtoAsync(RoomType t, CancellationToken ct)
    {
        var prices = await db.Set<Room>().AsNoTracking()
            .Where(r => r.RoomTypeId == t.Id && !r.IsArchived && r.Status != RoomStatus.OutOfService)
            .Select(r => new { Night = r.PriceOverride ?? t.BasePrice, Hour = r.HourlyPriceOverride ?? t.HourlyPrice })
            .ToListAsync(ct);
        if (prices.Count == 0)
        {
            return null;
        }

        var amenityIds = t.Amenities.Select(a => a.AmenityId).ToList();
        var amenities = await db.Set<Amenity>().AsNoTracking().Where(a => amenityIds.Contains(a.Id))
            .OrderBy(a => a.SortOrder)
            .Select(a => new AmenityDto(a.Id, a.Code, a.Name, a.Icon, a.SortOrder))
            .ToListAsync(ct);
        return new PublicRoomTypeDto(
            t.Id, t.Name, t.Description, t.Capacity, t.BedsCount, t.BedType.ToString(), prices.Min(p => p.Night), amenities, await store.PhotosAsync(t.Id, null, ct),
            prices.Where(p => p.Hour != null).Select(p => p.Hour).Min());
    }
}
