using Hms.Modules.Bookings.Contracts;
using Hms.Modules.Bookings.Domain;

namespace Hms.UnitTests.Bookings;

/// <summary>Штрафы, неявка, истечение и перенос брони (Sprint 5, roadmap §6.1 и §6.1a).</summary>
public class BookingRulesDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Start = new(2026, 12, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly PenaltyRules HourlyRules = new(TimeSpan.FromMinutes(120), 1, 1);

    private static readonly PenaltyRules NightlyRules = new(TimeSpan.FromHours(24), 1, 1);

    private static StayPeriod Hourly(DateTimeOffset start, int hours) =>
        new(BookingKind.Hourly, DateOnly.FromDateTime(start.UtcDateTime), DateOnly.FromDateTime(start.UtcDateTime), start, start.AddHours(hours), start.AddHours(hours).AddMinutes(30), hours);

    private static StayPeriod Nightly(int nights) =>
        new(BookingKind.Nightly, new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 1 + nights), Start, Start.AddDays(nights), Start.AddDays(nights), nights);

    /// <summary>Почасовая: 3 ч по 500 = 1500, скидка 300 → средняя цена часа 400.</summary>
    private static Booking HourlyBooking(bool confirm = true, DateTimeOffset? holdUntil = null) => Booking.Create(
        Guid.NewGuid(), "26-000001", BookingSource.Admin, Hourly(Start, 3),
        2, 0, new GuestContact("Гость", "+996555123456", null), Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(),
        new BookingPrice(1500m, 300m, 200m, 1400m, null, null, [], "{}"), confirm, Guid.NewGuid(), Now, holdUntil);

    /// <summary>Посуточная: 2 ночи, 6000.</summary>
    private static Booking NightlyBooking(bool confirm = true) => Booking.Create(
        Guid.NewGuid(), "26-000002", BookingSource.Admin, Nightly(2),
        2, 0, new GuestContact("Гость", "+996555123456", null), Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(),
        new BookingPrice(6000m, 0m, 0m, 6000m, null, null, [], "{}"), confirm, Guid.NewGuid(), Now);

    [Fact]
    public void Cancel_BeforeFreeDeadline_IsFree()
    {
        var booking = HourlyBooking();

        var terms = BookingPenalties.ForCancel(booking, HourlyRules, Start.AddHours(-2));

        terms.Should().BeEquivalentTo(new { IsFree = true, Penalty = 0m, FreeUntil = (DateTimeOffset?)Start.AddHours(-2) });
    }

    [Fact]
    public void Cancel_Late_Hourly_ChargesAverageHourPrice_AfterDiscount()
    {
        var booking = HourlyBooking();

        var terms = BookingPenalties.ForCancel(booking, HourlyRules, Start.AddMinutes(-90));

        terms.IsFree.Should().BeFalse();
        terms.Penalty.Should().Be(400m, "(1500 − 300) / 3 ч × 1 ч; услуги в штраф не входят");
        terms.Units.Should().Be(1);
    }

    [Fact]
    public void Cancel_Late_Nightly_ChargesOneNight()
    {
        var booking = NightlyBooking();

        BookingPenalties.ForCancel(booking, NightlyRules, Start.AddHours(-3)).Penalty.Should().Be(3000m);
    }

    [Fact]
    public void Penalty_NeverExceedsAccommodation()
    {
        var booking = NightlyBooking();

        BookingPenalties.Amount(booking, 5).Should().Be(6000m);
    }

    [Fact]
    public void Cancel_PendingBooking_IsAlwaysFree()
    {
        var booking = HourlyBooking(confirm: false);

        BookingPenalties.ForCancel(booking, HourlyRules, Start.AddHours(1)).Should().BeEquivalentTo(new { IsFree = true, Penalty = 0m });
    }

    [Fact]
    public void Cancel_WithPenalty_RecordsIt_AndFreesRoom()
    {
        var booking = HourlyBooking();

        booking.Cancel("Гость передумал", 400m, false, null, Start.AddMinutes(-30)).IsSuccess.Should().BeTrue();

        booking.Status.Should().Be(BookingStatus.Cancelled);
        booking.PenaltyAmount.Should().Be(400m);
        booking.PenaltyKind.Should().Be(PenaltyKind.LateCancel);
        booking.Rooms[0].IsBlocking.Should().BeFalse();
        booking.History.Should().Contain(h => h.EventType == "penalty_charged");
    }

    [Fact]
    public void Cancel_Waived_KeepsZeroPenalty_ButRemembersException()
    {
        var booking = HourlyBooking();

        booking.Cancel("Исключение", 400m, true, null, Start.AddMinutes(-30)).IsSuccess.Should().BeTrue();

        booking.PenaltyAmount.Should().Be(0m);
        booking.PenaltyWaived.Should().BeTrue();
        booking.History.Should().Contain(h => h.EventType == "penalty_waived");
    }

    [Fact]
    public void NoShow_BeforeStart_IsRejected()
    {
        var booking = HourlyBooking();

        booking.MarkNoShow(400m, false, null, Start.AddMinutes(-1)).Error!.Code.Should().Be("booking.no_show_too_early");
    }

    [Fact]
    public void NoShow_AfterStart_FreesRoom_AndChargesPenalty()
    {
        var booking = HourlyBooking();

        booking.MarkNoShow(400m, false, null, Start.AddMinutes(31)).IsSuccess.Should().BeTrue();

        booking.Status.Should().Be(BookingStatus.NoShow);
        booking.PenaltyAmount.Should().Be(400m);
        booking.PenaltyKind.Should().Be(PenaltyKind.NoShow);
        booking.Rooms[0].IsBlocking.Should().BeFalse();
    }

    [Fact]
    public void NoShow_PendingBooking_IsRejected()
    {
        var booking = HourlyBooking(confirm: false, holdUntil: Now.AddMinutes(30));

        booking.MarkNoShow(0m, false, null, Start.AddHours(1)).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Expire_OnlyAfterHold()
    {
        var booking = HourlyBooking(confirm: false, holdUntil: Now.AddMinutes(30));

        booking.Expire(Now.AddMinutes(29)).IsFailure.Should().BeTrue();
        booking.Expire(Now.AddMinutes(30)).IsSuccess.Should().BeTrue();
        booking.Status.Should().Be(BookingStatus.Expired);
        booking.Rooms[0].IsBlocking.Should().BeFalse();
    }

    [Fact]
    public void Confirm_ClearsHold_SoConfirmedBookingNeverExpires()
    {
        var booking = HourlyBooking(confirm: false, holdUntil: Now.AddMinutes(30));

        booking.Confirm(null, Now.AddMinutes(5));

        booking.HoldUntil.Should().BeNull();
        booking.Expire(Now.AddHours(1)).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void ConfirmedBooking_HasNoHold()
    {
        HourlyBooking(confirm: true, holdUntil: Now.AddMinutes(30)).HoldUntil.Should().BeNull();
    }

    [Fact]
    public void Reschedule_MovesTimeAndRoom_AndReprices()
    {
        var booking = HourlyBooking();
        var newRoom = Guid.NewGuid();
        var newStart = Start.AddHours(5);

        var result = booking.Reschedule(Hourly(newStart, 4), newRoom, Guid.NewGuid(),
            new BookingPrice(2000m, 0m, 0m, 2000m, null, null, [], "{}"), null, Now);

        result.IsSuccess.Should().BeTrue();
        booking.StartsAt.Should().Be(newStart);
        booking.EndsAt.Should().Be(newStart.AddHours(4));
        booking.Hours.Should().Be(4);
        booking.TotalAmount.Should().Be(2000m);
        booking.Rooms[0].RoomId.Should().Be(newRoom);
        booking.Rooms[0].BlockedUntil.Should().Be(newStart.AddHours(4).AddMinutes(30));
        booking.History.Should().Contain(h => h.EventType == "rescheduled");
    }

    [Fact]
    public void Reschedule_CannotChangeKind()
    {
        var booking = HourlyBooking();

        booking.Reschedule(Nightly(1), Guid.NewGuid(), Guid.NewGuid(), new BookingPrice(3000m, 0m, 0m, 3000m, null, null, [], "{}"), null, Now)
            .Error!.Code.Should().Be("booking.reschedule_kind");
    }

    [Fact]
    public void Reschedule_ExtendedBooking_IsRejected()
    {
        var booking = HourlyBooking();
        booking.Extend(Hourly(Start, 4), 1, 500m, "{}", null, Now).IsSuccess.Should().BeTrue();

        booking.Reschedule(Hourly(Start.AddHours(2), 4), Guid.NewGuid(), Guid.NewGuid(), new BookingPrice(2000m, 0m, 0m, 2000m, null, null, [], "{}"), null, Now)
            .Error!.Code.Should().Be("booking.reschedule_extended");
    }

    [Fact]
    public void Reschedule_CancelledBooking_IsRejected()
    {
        var booking = HourlyBooking();
        booking.Cancel("Отмена", null, Now);

        booking.Reschedule(Hourly(Start.AddHours(2), 3), Guid.NewGuid(), Guid.NewGuid(), new BookingPrice(1500m, 0m, 0m, 1500m, null, null, [], "{}"), null, Now)
            .Error!.Code.Should().Be("booking.reschedule_not_allowed");
    }
}
