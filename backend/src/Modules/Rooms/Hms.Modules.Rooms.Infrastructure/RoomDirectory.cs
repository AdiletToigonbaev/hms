using Hms.Modules.Hotel.Contracts;
using Hms.Modules.Rooms.Contracts;
using Hms.Modules.Rooms.Domain;
using Microsoft.EntityFrameworkCore;

namespace Hms.Modules.Rooms.Infrastructure;

/// <summary>Чтение номерного фонда другими модулями (IRoomDirectory).</summary>
internal sealed class RoomDirectory(DbContext db, IHotelContext hotel) : IRoomDirectory
{
    public async Task<RoomInfo?> GetAsync(Guid roomId, CancellationToken ct) =>
        (await QueryAsync(ct)).FirstOrDefault(r => r.Id == roomId) is { } found ? found : null;

    public async Task<IReadOnlyList<RoomInfo>> ListSellableAsync(Guid? roomTypeId, CancellationToken ct) =>
        (await QueryAsync(ct)).Where(r => r.IsSellable && (roomTypeId == null || r.RoomTypeId == roomTypeId)).ToList();

    public async Task<IReadOnlyList<RoomInfo>> ListActiveAsync(CancellationToken ct) =>
        (await QueryAsync(ct)).Where(r => !r.IsArchived && !r.RoomTypeArchived).OrderBy(r => r.Number.Length).ThenBy(r => r.Number, StringComparer.Ordinal).ToList();

    public async Task<bool> RoomTypeExistsAsync(Guid roomTypeId, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        return await db.Set<RoomType>().AnyAsync(t => t.Id == roomTypeId && t.HotelId == hotelId, ct);
    }

    public async Task<IReadOnlyDictionary<Guid, RoomInfo>> GetManyAsync(IEnumerable<Guid> roomIds, CancellationToken ct)
    {
        var ids = roomIds.ToHashSet();
        return (await QueryAsync(ct)).Where(r => ids.Contains(r.Id)).ToDictionary(r => r.Id);
    }

    // Номеров в гостинице 10–30: читаем весь фонд одним запросом, без N+1.
    private async Task<List<RoomInfo>> QueryAsync(CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        return await (
            from r in db.Set<Room>().AsNoTracking()
            join t in db.Set<RoomType>().AsNoTracking() on r.RoomTypeId equals t.Id
            where r.HotelId == hotelId
            select new RoomInfo(r.Id, r.Number, r.Floor, t.Id, t.Name, r.Capacity ?? t.Capacity, t.BasePrice, r.PriceOverride, r.Status, r.IsArchived, t.IsArchived, t.HourlyPrice, r.HourlyPriceOverride))
            .ToListAsync(ct);
    }
}
