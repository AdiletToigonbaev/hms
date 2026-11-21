using Hms.Modules.Audit.Contracts;
using Hms.Modules.Hotel.Contracts;
using Hms.Modules.Rooms.Application;
using Hms.Modules.Rooms.Contracts;
using Hms.Modules.Rooms.Domain;
using Hms.Platform.Security;
using Hms.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Hms.Modules.Rooms.Infrastructure;

/// <summary>Смена статуса номера по событиям других модулей; сохранение — в их транзакции.</summary>
internal sealed class RoomStatusChanger(DbContext db, IHotelContext hotel, ICurrentUser currentUser, IAuditTrail audit, TimeProvider time) : IRoomStatusChanger
{
    public async Task<Result> ChangeAsync(Guid roomId, RoomStatus target, RoomStatusSource source, string? reason, Guid? bookingId, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var room = await db.Set<Room>().FirstOrDefaultAsync(r => r.Id == roomId && r.HotelId == hotelId, ct);
        if (room is null)
        {
            return RoomErrors.RoomNotFound;
        }

        var from = room.Status;
        if (from == target)
        {
            return Result.Success();
        }

        var change = room.ChangeStatus(target, source, reason, currentUser.UserId, bookingId, time.GetUtcNow());
        if (change.IsFailure)
        {
            return change.Error;
        }

        db.Set<RoomStatusChange>().Add(change.Value);
        audit.Record(new AuditEntry("room.status_changed", "Room", roomId.ToString(),
            new { Status = from.ToString() }, new { Status = target.ToString(), Source = source.ToString(), reason, bookingId }));
        return Result.Success();
    }

    public async Task<RoomStatus?> GetStatusAsync(Guid roomId, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        return await db.Set<Room>().AsNoTracking().Where(r => r.Id == roomId && r.HotelId == hotelId).Select(r => (RoomStatus?)r.Status).FirstOrDefaultAsync(ct);
    }
}
