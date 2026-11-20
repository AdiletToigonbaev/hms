using Hms.Modules.Rooms.Contracts;

namespace Hms.Modules.Rooms.Domain;

/// <summary>
/// Допустимые переходы статуса номера (docs/database/database-design.md §4, ROOM-4).
/// Занятость меняют только заселение и выселение — вручную номер нельзя сделать занятым или освободить из «занят».
/// </summary>
public static class RoomStatusMachine
{
    private static readonly Dictionary<RoomStatusSource, (RoomStatus From, RoomStatus To)[]> Transitions = new()
    {
        [RoomStatusSource.Manual] =
        [
            (RoomStatus.Available, RoomStatus.Maintenance),
            (RoomStatus.Available, RoomStatus.OutOfService),
            (RoomStatus.Cleaning, RoomStatus.Available),
            (RoomStatus.Cleaning, RoomStatus.Maintenance),
            (RoomStatus.Cleaning, RoomStatus.OutOfService),
            (RoomStatus.Maintenance, RoomStatus.Available),
            (RoomStatus.Maintenance, RoomStatus.Cleaning),
            (RoomStatus.Maintenance, RoomStatus.OutOfService),
            (RoomStatus.OutOfService, RoomStatus.Available),
            (RoomStatus.OutOfService, RoomStatus.Cleaning),
            (RoomStatus.OutOfService, RoomStatus.Maintenance),
        ],
        [RoomStatusSource.CheckIn] = [(RoomStatus.Available, RoomStatus.Occupied)],
        [RoomStatusSource.CheckOut] = [(RoomStatus.Occupied, RoomStatus.Cleaning)],
        [RoomStatusSource.Housekeeping] = [(RoomStatus.Cleaning, RoomStatus.Available)],
    };

    public static bool CanTransition(RoomStatus from, RoomStatus to, RoomStatusSource source) =>
        Transitions.TryGetValue(source, out var allowed) && allowed.Contains((from, to));

    /// <summary>Куда можно перевести номер вручную из текущего статуса (для интерфейса).</summary>
    public static IReadOnlyList<RoomStatus> ManualTargets(RoomStatus from) =>
        Transitions[RoomStatusSource.Manual].Where(t => t.From == from).Select(t => t.To).ToList();
}
