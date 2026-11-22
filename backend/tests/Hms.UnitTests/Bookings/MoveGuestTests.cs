using Hms.Modules.Bookings.Contracts;
using Hms.Modules.Bookings.Domain;

namespace Hms.UnitTests.Bookings;

/// <summary>Переселение заселённого гостя: отрезки номеров, цена, запреты.</summary>
public class MoveGuestTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 21);
    private static readonly Guid OldRoom = Guid.NewGuid();
    private static readonly Guid NewRoom = Guid.NewGuid();

    /// <summary>Почасовая бронь 09:00–13:00 (4 ч по 500 = 2000), заселена в 09:00.</summary>
    private static Booking CheckedIn()
    {
        var start = Now.AddHours(1);
        var stay = new StayPeriod(BookingKind.Hourly, Today, Today, start, start.AddHours(4), start.AddHours(4).AddMinutes(30), 4);
        var booking = Booking.Create(Guid.NewGuid(), "Ч-2111-01", BookingSource.Admin, stay, 2, 0, new GuestContact("Гость", "+996555123456", null), null, null,
            OldRoom, Guid.NewGuid(), new BookingPrice(2000m, 0m, 0m, 2000m, null, null, [], "{}"), true, null, Now);
        booking.CheckInGuest(null, start).IsSuccess.Should().BeTrue();
        return booking;
    }

    [Fact]
    public void Move_SplitsStayIntoTwoRoomSegments_AndAppliesPriceDifference()
    {
        var booking = CheckedIn();
        var at = Now.AddHours(2); // 10:00 — час в старом номере

        var moved = booking.MoveGuest(NewRoom, Guid.NewGuid(), Today, at, at.AddMinutes(30), booking.EndsAt.AddMinutes(30), 600m, "Шумно", null);

        moved.IsSuccess.Should().BeTrue();
        booking.Rooms.Should().HaveCount(2);
        var old = booking.Rooms.Single(r => r.RoomId == OldRoom);
        old.EndsAt.Should().Be(at, "в старом номере — до момента переселения");
        old.BlockedUntil.Should().Be(at.AddMinutes(30), "старый номер занят ещё на время уборки");
        booking.CurrentRoom.RoomId.Should().Be(NewRoom);
        booking.CurrentRoom.StartsAt.Should().Be(at);
        booking.CurrentRoom.EndsAt.Should().Be(booking.EndsAt);
        booking.TotalAmount.Should().Be(2600m);
        booking.RoomChangeAmount.Should().Be(600m);
        booking.History.Should().Contain(h => h.EventType == "room_moved");
    }

    [Fact]
    public void Move_WithoutRecalculation_KeepsPrice_AndCannotGoBelowZero()
    {
        var free = CheckedIn();
        free.MoveGuest(NewRoom, Guid.NewGuid(), Today, Now.AddHours(2), Now.AddHours(2), free.EndsAt, 0m, null, null);
        free.TotalAmount.Should().Be(2000m);

        var cheaper = CheckedIn();
        cheaper.MoveGuest(NewRoom, Guid.NewGuid(), Today, Now.AddHours(2), Now.AddHours(2), cheaper.EndsAt, -5000m, null, null);
        cheaper.TotalAmount.Should().Be(0m, "стоимость не уходит в минус");
    }

    [Fact]
    public void Move_Rules()
    {
        var booking = CheckedIn();
        booking.MoveGuest(OldRoom, Guid.NewGuid(), Today, Now.AddHours(2), Now.AddHours(2), booking.EndsAt, 0m, null, null).Error!.Code
            .Should().Be("booking.move_same_room");
        booking.MoveGuest(NewRoom, Guid.NewGuid(), Today, booking.EndsAt, booking.EndsAt, booking.EndsAt, 0m, null, null).Error!.Code
            .Should().Be("booking.move_too_late");

        var start = Now.AddHours(1);
        var notCheckedIn = Booking.Create(Guid.NewGuid(), "Ч-2111-02", BookingSource.Admin,
            new StayPeriod(BookingKind.Hourly, Today, Today, start, start.AddHours(2), start.AddHours(2), 2), 2, 0, new GuestContact("Гость", "+996555123456", null),
            null, null, OldRoom, Guid.NewGuid(), new BookingPrice(1000m, 0m, 0m, 1000m, null, null, [], "{}"), true, null, Now);
        notCheckedIn.MoveGuest(NewRoom, Guid.NewGuid(), Today, Now, Now, start.AddHours(2), 0m, null, null).Error!.Code
            .Should().Be("booking.move_not_checked_in");
    }

    [Fact]
    public void EarlyCheckIn_MovedBeforeStart_JustChangesRoom()
    {
        var start = Now.AddHours(1);
        var booking = Booking.Create(Guid.NewGuid(), "Ч-2111-03", BookingSource.Admin,
            new StayPeriod(BookingKind.Hourly, Today, Today, start, start.AddHours(2), start.AddHours(2), 2), 2, 0, new GuestContact("Гость", "+996555123456", null),
            null, null, OldRoom, Guid.NewGuid(), new BookingPrice(1000m, 0m, 0m, 1000m, null, null, [], "{}"), true, null, Now);
        booking.CheckInGuest(null, Now.AddMinutes(30));

        booking.MoveGuest(NewRoom, Guid.NewGuid(), Today, Now.AddMinutes(40), Now.AddMinutes(40), booking.EndsAt, 0m, null, null).IsSuccess.Should().BeTrue();

        booking.Rooms.Should().ContainSingle(r => r.RoomId == NewRoom, "до начала брони — без разделения на отрезки");
    }

    [Fact]
    public void Extend_AfterMove_ExtendsOnlyCurrentRoom()
    {
        var booking = CheckedIn();
        var at = Now.AddHours(2);
        booking.MoveGuest(NewRoom, Guid.NewGuid(), Today, at, at, booking.EndsAt, 0m, null, null);
        var oldEnd = booking.Rooms.Single(r => r.RoomId == OldRoom).EndsAt;

        var extended = new StayPeriod(BookingKind.Hourly, Today, Today, booking.StartsAt, booking.EndsAt.AddHours(1), booking.EndsAt.AddHours(1), 5);
        booking.Extend(extended, 1, 500m, "{}", null, at).IsSuccess.Should().BeTrue();

        booking.Rooms.Single(r => r.RoomId == OldRoom).EndsAt.Should().Be(oldEnd);
        booking.CurrentRoom.EndsAt.Should().Be(extended.EndsAt);
    }

    [Theory]
    [InlineData(180, 6)] // ровно 3 ч
    [InlineData(160, 6)] // 2 ч 40 мин → 3 ч
    [InlineData(150, 5)] // 2 ч 30 мин → 2,5 ч
    [InlineData(130, 5)] // 2 ч 10 мин → 2,5 ч
    [InlineData(121, 5)] // 2 ч 01 мин → 2,5 ч
    [InlineData(10, 1)] // 10 мин → полчаса (минимум)
    public void RemainingTime_RoundsUpToHalfHour(int minutesLeft, int halfHours)
    {
        RoomMoveTime.RemainingHalfHours(Now, Now.AddMinutes(minutesLeft)).Should().Be(halfHours);
    }
}
