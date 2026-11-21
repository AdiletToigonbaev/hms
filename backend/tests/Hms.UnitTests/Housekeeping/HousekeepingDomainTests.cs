using Hms.Modules.Bookings.Contracts;
using Hms.Modules.Bookings.Domain;
using Hms.Modules.Housekeeping.Domain;

namespace Hms.UnitTests.Housekeeping;

/// <summary>Задача уборки и заселение/выселение брони (Sprint 7).</summary>
public class HousekeepingDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Maid = Guid.NewGuid();

    private static HousekeepingTask NewTask() =>
        HousekeepingTask.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), TaskOrigin.CheckOut, TaskPriority.Normal, Now.AddMinutes(30), null,
            ["Сменить бельё", "Убрать санузел"], null, Now);

    [Fact]
    public void Flow_TakeStartChecklistComplete_ThenInspect()
    {
        var task = NewTask();

        task.Take(Maid).IsSuccess.Should().BeTrue();
        task.Status.Should().Be(HousekeepingTaskStatus.Assigned);
        task.Start(Maid, Now).IsSuccess.Should().BeTrue();
        task.Complete(Maid, Now).Error!.Code.Should().Be("housekeeping.checklist_incomplete");
        foreach (var item in task.Items)
        {
            task.SetItem(item.Id, true, Maid, Now).IsSuccess.Should().BeTrue();
        }

        task.Complete(Maid, Now).IsSuccess.Should().BeTrue();
        task.Inspect(approved: true, null, Guid.NewGuid(), Now).IsSuccess.Should().BeTrue();
        task.Status.Should().Be(HousekeepingTaskStatus.Inspected);
        task.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void Reject_NeedsReason_ResetsChecklist_AndMaidRedoes()
    {
        var task = NewTask();
        task.Assign(Maid);
        task.Start(Maid, Now);
        foreach (var item in task.Items)
        {
            task.SetItem(item.Id, true, Maid, Now);
        }

        task.Complete(Maid, Now);

        task.Inspect(false, " ", null, Now).Error!.Code.Should().Be("housekeeping.reason_required");
        task.Inspect(false, "Пыль на полке", null, Now).IsSuccess.Should().BeTrue();
        task.Status.Should().Be(HousekeepingTaskStatus.Rejected);
        task.Items.Should().OnlyContain(i => !i.IsDone);
        task.Start(Maid, Now).IsSuccess.Should().BeTrue("уборщик убирает заново");
    }

    [Fact]
    public void OtherMaid_CannotWorkOnTask()
    {
        var task = NewTask();
        task.Assign(Maid);

        task.Start(Guid.NewGuid(), Now).Error!.Code.Should().Be("housekeeping.not_your_task");
    }

    [Fact]
    public void Delete_OnlyBeforeStart()
    {
        var started = NewTask();
        started.Assign(Maid);
        started.Start(Maid, Now);
        started.Delete().IsFailure.Should().BeTrue();

        var fresh = NewTask();
        fresh.Delete().IsSuccess.Should().BeTrue();
        fresh.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public void AutoAccept_ClosesCompletedTaskWithoutInspector()
    {
        var task = NewTask();
        task.Assign(Maid);
        task.Start(Maid, Now);
        foreach (var item in task.Items)
        {
            task.SetItem(item.Id, true, Maid, Now);
        }

        task.Complete(Maid, Now);
        task.AutoAccept(Now);

        task.Status.Should().Be(HousekeepingTaskStatus.Inspected);
        task.InspectedBy.Should().BeNull();
    }

    private static Booking HourlyBooking(bool confirm = true)
    {
        var start = Now.AddHours(1);
        var stay = new StayPeriod(BookingKind.Hourly, new DateOnly(2026, 11, 21), new DateOnly(2026, 11, 21), start, start.AddHours(4), start.AddHours(4).AddMinutes(30), 4);
        return Booking.Create(Guid.NewGuid(), "Ч-2111-01", BookingSource.Admin, stay, 2, 0, new GuestContact("Гость", "+996555123456", null), null, null,
            Guid.NewGuid(), Guid.NewGuid(), new BookingPrice(2000m, 0m, 0m, 2000m, null, null, [], "{}"), confirm, null, Now);
    }

    [Fact]
    public void CheckIn_RequiresConfirmed_AndRecordsActualTime()
    {
        HourlyBooking(confirm: false).CheckInGuest(null, Now).Error!.Code.Should().Be("booking.checkin_not_confirmed");

        var booking = HourlyBooking();
        booking.CheckInGuest(null, Now.AddMinutes(50)).IsSuccess.Should().BeTrue();
        booking.Status.Should().Be(BookingStatus.CheckedIn);
        booking.ActualCheckInAt.Should().Be(Now.AddMinutes(50));
        booking.Rooms[0].IsBlocking.Should().BeTrue("гость в номере — номер занят");
    }

    [Fact]
    public void EarlyCheckOut_ShortensStay_AndAppliesCredit()
    {
        var booking = HourlyBooking();
        booking.CheckInGuest(null, Now.AddHours(1));
        var leave = Now.AddHours(2);
        var shortened = booking.Rooms[0] is { } line
            ? new StayPeriod(BookingKind.Hourly, booking.CheckIn, booking.CheckOut, booking.StartsAt, leave, leave.AddMinutes(30), 4)
            : null;

        booking.CheckOutGuest(null, leave, shortened, 1000m).IsSuccess.Should().BeTrue();

        booking.Status.Should().Be(BookingStatus.CheckedOut);
        booking.EndsAt.Should().Be(leave);
        booking.ActualCheckOutAt.Should().Be(leave);
        booking.EarlyDepartureCredit.Should().Be(1000m);
        booking.TotalAmount.Should().Be(1000m);
        booking.Rooms[0].IsBlocking.Should().BeFalse();
        booking.History.Should().Contain(h => h.EventType == "early_departure");
    }
}
