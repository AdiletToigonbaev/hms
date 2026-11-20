using System.Data.Common;
using Hms.Modules.Audit.Contracts;
using Hms.Modules.Hotel.Contracts;
using Hms.Modules.Rooms.Application;
using Hms.Modules.Rooms.Domain;
using Hms.Platform.Files;
using Hms.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Hms.Modules.Rooms.Infrastructure;

/// <summary>Общие операции с фото и сохранением.</summary>
internal sealed class RoomsStore(DbContext db, IFileService files, IFileStorage storage)
{
    public const int MaxPhotos = 20;

    public DbContext Db => db;

    public static string FileUrl(Guid fileId) => $"/api/v1/files/{fileId}";

    public async Task<IReadOnlyList<PhotoDto>> PhotosAsync(Guid? roomTypeId, Guid? roomId, CancellationToken ct) =>
        (await db.Set<RoomPhoto>().AsNoTracking()
            .Where(p => (roomTypeId != null && p.RoomTypeId == roomTypeId) || (roomId != null && p.RoomId == roomId))
            .OrderBy(p => p.SortOrder)
            .ToListAsync(ct))
        .Select(ToDto)
        .ToList();

    public async Task<Result<PhotoDto>> AddPhotoAsync(Guid? roomTypeId, Guid? roomId, Stream content, string fileName, CancellationToken ct)
    {
        var photos = db.Set<RoomPhoto>().Where(p => (roomTypeId != null && p.RoomTypeId == roomTypeId) || (roomId != null && p.RoomId == roomId));
        var count = await photos.CountAsync(ct);
        if (count >= MaxPhotos)
        {
            return RoomErrors.TooManyPhotos;
        }

        var saved = await files.SaveImageAsync(content, fileName, FileVisibility.Public, ct);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        var order = count == 0 ? 0 : await photos.MaxAsync(p => p.SortOrder, ct) + 1;
        var photo = roomTypeId is { } typeId
            ? RoomPhoto.ForRoomType(typeId, saved.Value.Id, order)
            : RoomPhoto.ForRoom(roomId!.Value, saved.Value.Id, order);
        db.Set<RoomPhoto>().Add(photo);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // Не оставляем «осиротевший» файл на диске, если запись в БД не удалась.
            storage.Delete(saved.Value.Path);
            throw;
        }

        return ToDto(photo);
    }

    public async Task<Result> DeletePhotoAsync(Guid? roomTypeId, Guid? roomId, Guid photoId, CancellationToken ct)
    {
        var photo = await db.Set<RoomPhoto>().FirstOrDefaultAsync(
            p => p.Id == photoId && ((roomTypeId != null && p.RoomTypeId == roomTypeId) || (roomId != null && p.RoomId == roomId)), ct);
        if (photo is null)
        {
            return RoomErrors.PhotoNotFound;
        }

        var file = await db.Set<StoredFile>().FirstOrDefaultAsync(f => f.Id == photo.FileId, ct);
        db.Set<RoomPhoto>().Remove(photo);
        if (file is not null)
        {
            db.Set<StoredFile>().Remove(file);
        }

        await db.SaveChangesAsync(ct);
        if (file is not null)
        {
            storage.Delete(file.Path); // после фиксации в БД
        }

        return Result.Success();
    }

    /// <summary>Сохранение с переводом конфликтов в доменные ошибки.</summary>
    public async Task<Error?> SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return RoomErrors.ConcurrencyConflict;
        }
        catch (DbUpdateException ex) when (ex.InnerException is DbException { SqlState: "23505" } pg && pg.Message.Contains("number", StringComparison.OrdinalIgnoreCase))
        {
            db.ChangeTracker.Clear();
            return RoomErrors.NumberTaken;
        }
        catch (DbUpdateException ex) when (ex.InnerException is DbException { SqlState: "23505" })
        {
            db.ChangeTracker.Clear();
            return RoomErrors.AmenityCodeTaken;
        }
    }

    public static PhotoDto ToDto(RoomPhoto p) => new(p.Id, p.FileId, FileUrl(p.FileId), p.SortOrder);
}

internal sealed class AmenityService(DbContext db, IHotelContext hotel, IAuditTrail audit, RoomsStore store) : IAmenityService
{
    public async Task<IReadOnlyList<AmenityDto>> ListAsync(CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        return await db.Set<Amenity>().AsNoTracking().Where(a => a.HotelId == hotelId)
            .OrderBy(a => a.SortOrder).ThenBy(a => a.Name)
            .Select(a => new AmenityDto(a.Id, a.Code, a.Name, a.Icon, a.SortOrder))
            .ToListAsync(ct);
    }

    public async Task<Result<AmenityDto>> CreateAsync(CreateAmenityRequest r, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        if (await db.Set<Amenity>().AnyAsync(a => a.HotelId == hotelId && a.Code == r.Code, ct))
        {
            return RoomErrors.AmenityCodeTaken;
        }

        var amenity = new Amenity(hotelId, r.Code, r.Name.Trim(), string.IsNullOrWhiteSpace(r.Icon) ? null : r.Icon.Trim(), r.SortOrder);
        db.Set<Amenity>().Add(amenity);
        audit.Record(new AuditEntry("amenity.created", "Amenity", amenity.Id.ToString(), After: new { amenity.Code, amenity.Name }));
        return await store.SaveAsync(ct) is { } error ? error : ToDto(amenity);
    }

    public async Task<Result<AmenityDto>> UpdateAsync(Guid id, UpdateAmenityRequest r, CancellationToken ct)
    {
        var amenity = await FindAsync(id, ct);
        if (amenity is null)
        {
            return RoomErrors.AmenityNotFound;
        }

        amenity.Update(r.Name, r.Icon, r.SortOrder);
        return await store.SaveAsync(ct) is { } error ? error : ToDto(amenity);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct)
    {
        var amenity = await FindAsync(id, ct);
        if (amenity is null)
        {
            return RoomErrors.AmenityNotFound;
        }

        if (await db.Set<RoomTypeAmenity>().AnyAsync(a => a.AmenityId == id, ct))
        {
            return RoomErrors.AmenityInUse;
        }

        db.Set<Amenity>().Remove(amenity);
        audit.Record(new AuditEntry("amenity.deleted", "Amenity", id.ToString(), Before: new { amenity.Code, amenity.Name }));
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<Amenity?> FindAsync(Guid id, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        return await db.Set<Amenity>().FirstOrDefaultAsync(a => a.Id == id && a.HotelId == hotelId, ct);
    }

    private static AmenityDto ToDto(Amenity a) => new(a.Id, a.Code, a.Name, a.Icon, a.SortOrder);
}

internal sealed class RoomTypeService(DbContext db, IHotelContext hotel, IAuditTrail audit, RoomsStore store, TimeProvider time) : IRoomTypeService
{
    public async Task<IReadOnlyList<RoomTypeDto>> ListAsync(bool includeArchived, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var types = await db.Set<RoomType>().AsNoTracking().Include(t => t.Amenities)
            .Where(t => t.HotelId == hotelId && (includeArchived || !t.IsArchived))
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .ToListAsync(ct);
        var result = new List<RoomTypeDto>(types.Count);
        foreach (var type in types)
        {
            result.Add(await ToDtoAsync(type, ct));
        }

        return result;
    }

    public async Task<Result<RoomTypeDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var type = await FindAsync(id, tracking: false, ct);
        return type is null ? RoomErrors.RoomTypeNotFound : await ToDtoAsync(type, ct);
    }

    public async Task<Result<RoomTypeDto>> CreateAsync(SaveRoomTypeRequest r, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var type = new RoomType(await hotel.GetHotelIdAsync(ct), r.Name.Trim(), now);
        type.Update(r.Name, r.Description, r.Capacity, r.BedsCount, Enum.Parse<BedType>(r.BedType), Money.Kgs(r.BasePrice), r.SortOrder, now);
        type.SetHourlyPrice(r.HourlyPrice is { } hourly ? Money.Kgs(hourly) : null);
        db.Set<RoomType>().Add(type);
        audit.Record(new AuditEntry("room_type.created", "RoomType", type.Id.ToString(), After: Snapshot(type)));
        return await store.SaveAsync(ct) is { } error ? error : await ToDtoAsync(type, ct);
    }

    public async Task<Result<RoomTypeDto>> UpdateAsync(Guid id, SaveRoomTypeRequest r, CancellationToken ct)
    {
        var type = await FindAsync(id, tracking: true, ct);
        if (type is null)
        {
            return RoomErrors.RoomTypeNotFound;
        }

        if (r.Version is { } version)
        {
            db.Entry(type).Property(t => t.Version).OriginalValue = version;
        }

        var before = Snapshot(type);
        type.Update(r.Name, r.Description, r.Capacity, r.BedsCount, Enum.Parse<BedType>(r.BedType), Money.Kgs(r.BasePrice), r.SortOrder, time.GetUtcNow());
        type.SetHourlyPrice(r.HourlyPrice is { } hourly ? Money.Kgs(hourly) : null);
        audit.Record(new AuditEntry("room_type.updated", "RoomType", type.Id.ToString(), before, Snapshot(type)));
        return await store.SaveAsync(ct) is { } error ? error : await ToDtoAsync(type, ct);
    }

    public async Task<Result<RoomTypeDto>> SetArchivedAsync(Guid id, bool archived, CancellationToken ct)
    {
        var type = await FindAsync(id, tracking: true, ct);
        if (type is null)
        {
            return RoomErrors.RoomTypeNotFound;
        }

        if (archived && await db.Set<Room>().AnyAsync(r => r.RoomTypeId == id && !r.IsArchived, ct))
        {
            return RoomErrors.RoomTypeHasActiveRooms;
        }

        if (archived)
        {
            type.Archive(time.GetUtcNow());
        }
        else
        {
            type.Restore(time.GetUtcNow());
        }

        audit.Record(new AuditEntry(archived ? "room_type.archived" : "room_type.restored", "RoomType", id.ToString()));
        return await store.SaveAsync(ct) is { } error ? error : await ToDtoAsync(type, ct);
    }

    public async Task<Result<RoomTypeDto>> SetAmenitiesAsync(Guid id, SetAmenitiesRequest r, CancellationToken ct)
    {
        var type = await FindAsync(id, tracking: true, ct);
        if (type is null)
        {
            return RoomErrors.RoomTypeNotFound;
        }

        var ids = r.AmenityIds.Distinct().ToList();
        var known = await db.Set<Amenity>().CountAsync(a => a.HotelId == type.HotelId && ids.Contains(a.Id), ct);
        if (known != ids.Count)
        {
            return RoomErrors.UnknownAmenity;
        }

        var before = type.Amenities.Select(a => a.AmenityId).ToList();
        type.SetAmenities(ids, time.GetUtcNow());
        audit.Record(new AuditEntry("room_type.amenities_changed", "RoomType", id.ToString(), before, ids));
        return await store.SaveAsync(ct) is { } error ? error : await ToDtoAsync(type, ct);
    }

    public async Task<Result<PhotoDto>> AddPhotoAsync(Guid id, Stream content, string fileName, CancellationToken ct)
    {
        if (await FindAsync(id, tracking: false, ct) is null)
        {
            return RoomErrors.RoomTypeNotFound;
        }

        var result = await store.AddPhotoAsync(id, null, content, fileName, ct);
        if (result.IsSuccess)
        {
            await audit.RecordNowAsync(new AuditEntry("room_type.photo_added", "RoomType", id.ToString(), After: new { result.Value.FileId }), ct);
        }

        return result;
    }

    public async Task<Result> DeletePhotoAsync(Guid id, Guid photoId, CancellationToken ct)
    {
        if (await FindAsync(id, tracking: false, ct) is null)
        {
            return RoomErrors.RoomTypeNotFound;
        }

        var result = await store.DeletePhotoAsync(id, null, photoId, ct);
        if (result.IsSuccess)
        {
            await audit.RecordNowAsync(new AuditEntry("room_type.photo_deleted", "RoomType", id.ToString(), Before: new { photoId }), ct);
        }

        return result;
    }

    public async Task<Result<RoomTypeDto>> ReorderPhotosAsync(Guid id, ReorderPhotosRequest r, CancellationToken ct)
    {
        var type = await FindAsync(id, tracking: false, ct);
        if (type is null)
        {
            return RoomErrors.RoomTypeNotFound;
        }

        var photos = await db.Set<RoomPhoto>().Where(p => p.RoomTypeId == id).ToListAsync(ct);
        if (photos.Count != r.PhotoIds.Count || photos.Any(p => !r.PhotoIds.Contains(p.Id)))
        {
            return RoomErrors.PhotoOrderMismatch;
        }

        foreach (var photo in photos)
        {
            photo.MoveTo(r.PhotoIds.ToList().IndexOf(photo.Id));
        }

        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(type, ct);
    }

    private async Task<RoomType?> FindAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var query = db.Set<RoomType>().Include(t => t.Amenities).Where(t => t.Id == id && t.HotelId == hotelId);
        return await (tracking ? query : query.AsNoTracking()).FirstOrDefaultAsync(ct);
    }

    private async Task<RoomTypeDto> ToDtoAsync(RoomType t, CancellationToken ct)
    {
        var amenityIds = t.Amenities.Select(a => a.AmenityId).ToList();
        var amenities = await db.Set<Amenity>().AsNoTracking().Where(a => amenityIds.Contains(a.Id))
            .OrderBy(a => a.SortOrder)
            .Select(a => new AmenityDto(a.Id, a.Code, a.Name, a.Icon, a.SortOrder))
            .ToListAsync(ct);
        var roomCount = await db.Set<Room>().CountAsync(r => r.RoomTypeId == t.Id && !r.IsArchived, ct);
        return new RoomTypeDto(
            t.Id, t.Name, t.Description, t.Capacity, t.BedsCount, t.BedType.ToString(), t.BasePrice, t.SortOrder, t.IsArchived,
            roomCount, amenities, await store.PhotosAsync(t.Id, null, ct), t.Version, t.HourlyPrice);
    }

    private static object Snapshot(RoomType t) => new { t.Name, t.Capacity, t.BedsCount, BedType = t.BedType.ToString(), t.BasePrice, t.HourlyPrice, t.SortOrder };
}
