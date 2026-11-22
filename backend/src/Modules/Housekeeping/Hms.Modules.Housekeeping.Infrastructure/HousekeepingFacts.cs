using Hms.Modules.Hotel.Contracts;
using Hms.Modules.Housekeeping.Contracts;
using Hms.Modules.Housekeeping.Domain;
using Microsoft.EntityFrameworkCore;

namespace Hms.Modules.Housekeeping.Infrastructure;

internal sealed class HousekeepingFacts(DbContext db, IHotelContext hotel) : IHousekeepingFacts
{
    public async Task<IReadOnlyList<CleaningFact>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        return await db.Set<HousekeepingTask>().AsNoTracking()
            .Where(t => t.HotelId == hotelId && ((t.CreatedAt >= from && t.CreatedAt < to) || (t.CompletedAt >= from && t.CompletedAt < to)))
            .Select(t => new CleaningFact(t.Id, t.RoomId, t.Status.ToString(), t.Origin.ToString(), t.AssignedTo, t.InspectedBy,
                t.CreatedAt, t.StartedAt, t.CompletedAt, t.InspectedAt))
            .ToListAsync(ct);
    }

    public async Task<int> OpenCountAsync(CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        return await db.Set<HousekeepingTask>().CountAsync(t => t.HotelId == hotelId && t.Status != HousekeepingTaskStatus.Inspected, ct);
    }
}
