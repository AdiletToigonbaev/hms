using Hms.Modules.Rooms.Contracts;
using Hms.Modules.Rooms.Domain;
using Hms.Platform.Files;

namespace Hms.UnitTests.Rooms;

public class RoomDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

    private static Room NewRoom() => new(Guid.NewGuid(), Guid.NewGuid(), "101", Now);

    [Theory]
    [InlineData(RoomStatus.Available, RoomStatus.Occupied, RoomStatusSource.CheckIn, true)]
    [InlineData(RoomStatus.Occupied, RoomStatus.Cleaning, RoomStatusSource.CheckOut, true)]
    [InlineData(RoomStatus.Cleaning, RoomStatus.Available, RoomStatusSource.Housekeeping, true)]
    [InlineData(RoomStatus.Available, RoomStatus.Occupied, RoomStatusSource.Manual, false)]
    [InlineData(RoomStatus.Occupied, RoomStatus.Available, RoomStatusSource.Manual, false)]
    [InlineData(RoomStatus.Occupied, RoomStatus.Maintenance, RoomStatusSource.Manual, false)]
    [InlineData(RoomStatus.Available, RoomStatus.Maintenance, RoomStatusSource.Manual, true)]
    [InlineData(RoomStatus.Maintenance, RoomStatus.Available, RoomStatusSource.Manual, true)]
    [InlineData(RoomStatus.Cleaning, RoomStatus.Available, RoomStatusSource.Manual, true)]
    [InlineData(RoomStatus.Available, RoomStatus.Cleaning, RoomStatusSource.Manual, false)]
    [InlineData(RoomStatus.Available, RoomStatus.Available, RoomStatusSource.Manual, false)]
    public void StateMachine_AllowsOnlyDocumentedTransitions(RoomStatus from, RoomStatus to, RoomStatusSource source, bool allowed)
    {
        RoomStatusMachine.CanTransition(from, to, source).Should().Be(allowed);
    }

    [Fact]
    public void ManualTargets_NeverIncludeOccupied()
    {
        foreach (var status in Enum.GetValues<RoomStatus>())
        {
            RoomStatusMachine.ManualTargets(status).Should().NotContain(RoomStatus.Occupied);
        }

        RoomStatusMachine.ManualTargets(RoomStatus.Occupied).Should().BeEmpty("из «занят» номер выводит только выселение");
    }

    [Fact]
    public void ChangeStatus_Manual_RequiresReason_AndProducesHistory()
    {
        var room = NewRoom();
        var by = Guid.NewGuid();

        room.ChangeStatus(RoomStatus.Maintenance, RoomStatusSource.Manual, "  ", by, null, Now).Error!.Code.Should().Be("room.reason_required");
        room.Status.Should().Be(RoomStatus.Available, "при ошибке статус не меняется");

        var change = room.ChangeStatus(RoomStatus.Maintenance, RoomStatusSource.Manual, " Течёт кран ", by, null, Now.AddMinutes(1));
        change.IsSuccess.Should().BeTrue();
        change.Value.Should().BeEquivalentTo(new { FromStatus = RoomStatus.Available, ToStatus = RoomStatus.Maintenance, Reason = "Течёт кран", ChangedBy = by });
        room.Status.Should().Be(RoomStatus.Maintenance);
    }

    [Fact]
    public void ChangeStatus_CheckInAndOut_DoNotRequireReason()
    {
        var room = NewRoom();
        var booking = Guid.NewGuid();

        room.ChangeStatus(RoomStatus.Occupied, RoomStatusSource.CheckIn, null, null, booking, Now).IsSuccess.Should().BeTrue();
        room.ChangeStatus(RoomStatus.Cleaning, RoomStatusSource.CheckOut, null, null, booking, Now).Value.BookingId.Should().Be(booking);
    }

    [Fact]
    public void Archive_NotAllowed_ForOccupiedOrCleaning_AndArchivedRoomRejectsStatusChanges()
    {
        var room = NewRoom();
        room.ChangeStatus(RoomStatus.Occupied, RoomStatusSource.CheckIn, null, null, null, Now);
        room.Archive(Now).Error.Should().Be(Room.ArchiveNotAllowed);

        var free = NewRoom();
        free.Archive(Now).IsSuccess.Should().BeTrue();
        free.ChangeStatus(RoomStatus.Maintenance, RoomStatusSource.Manual, "x", null, null, Now).Error!.Code.Should().Be("room.archived");
    }

    [Fact]
    public void RoomType_SetAmenities_ReplacesSet()
    {
        var type = new RoomType(Guid.NewGuid(), "Стандарт", Now);
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();

        type.SetAmenities([a, b], Now);
        type.SetAmenities([b, c, c], Now);

        type.Amenities.Select(x => x.AmenityId).Should().BeEquivalentTo([b, c]);
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0 }, "image/png")]
    [InlineData(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P' }, "image/webp")]
    public void ImageSignature_DetectsByContent(byte[] bytes, string expected)
    {
        ImageSignature.Detect(bytes)!.Value.ContentType.Should().Be(expected);
    }

    [Theory]
    [InlineData(new byte[] { (byte)'<', (byte)'s', (byte)'v', (byte)'g' })]
    [InlineData(new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8' })]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46 })]
    [InlineData(new byte[0])]
    public void ImageSignature_RejectsSvgGifPdfAndEmpty(byte[] bytes)
    {
        ImageSignature.Detect(bytes).Should().BeNull();
    }
}
