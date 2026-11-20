using Hms.Modules.Bookings.Contracts;
using Hms.Modules.Bookings.Domain;
using Hms.Modules.Pricing.Contracts;
using Hms.Modules.Pricing.Domain;
using Hms.SharedKernel;

namespace Hms.UnitTests.Bookings;

public class PricingCalculationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid TypeId = Guid.NewGuid();

    // 2026-12-04 — пятница, 05 — суббота, 06 — воскресенье
    private static DateRange Stay(int fromDay, int toDay) => new(new DateOnly(2026, 12, fromDay), new DateOnly(2026, 12, toDay));

    private static RoomRate Rate(string name, int from, int to, WeekDays days, decimal price, int priority) =>
        new(Guid.NewGuid(), TypeId, name, new DateOnly(2026, 12, from), new DateOnly(2026, 12, to), days, price, priority, Now);

    [Fact]
    public void NoRates_UsesRoomOverride_ElseBasePrice()
    {
        var withBase = PriceCalculation.Calculate(Stay(1, 4), 3000m, null, [], null, []);
        var withOverride = PriceCalculation.Calculate(Stay(1, 4), 3000m, 3500m, [], null, []);

        withBase.AccommodationTotal.Should().Be(9000m);
        withBase.Nights.Should().OnlyContain(n => n.Source == PriceCalculation.BasePriceSource);
        withOverride.Total.Should().Be(10500m);
    }

    [Fact]
    public void SeasonAndWeekendRates_HigherPriorityWins_PerNight()
    {
        var season = Rate("Новогодний", 1, 31, WeekDays.All, 4000m, 1);
        var weekend = Rate("Выходные", 1, 31, WeekDays.Weekend, 5000m, 2);

        var quote = PriceCalculation.Calculate(Stay(3, 7), 3000m, 3500m, [season, weekend], null, []);

        // 3 — чт (сезон), 4 — пт (выходные), 5 — сб (выходные), 6 — вс (сезон: в маске Weekend только пт и сб)
        quote.Nights.Select(n => n.Price).Should().Equal(4000m, 5000m, 5000m, 4000m);
        quote.Nights[1].Source.Should().Be("Тариф «Выходные»");
        quote.AccommodationTotal.Should().Be(18000m);
    }

    [Fact]
    public void InactiveOrOutOfPeriodRate_IsIgnored()
    {
        var past = Rate("Прошлый", 1, 2, WeekDays.All, 9999m, 10);
        var inactive = Rate("Выключен", 1, 31, WeekDays.All, 9999m, 10);
        inactive.Update("Выключен", inactive.DateFrom, inactive.DateTo, WeekDays.All, 9999m, 10, isActive: false, Now);

        PriceCalculation.Calculate(Stay(3, 5), 3000m, null, [past, inactive], null, []).AccommodationTotal.Should().Be(6000m);
    }

    [Fact]
    public void PercentDiscount_RoundsToCents_AndAppliesOnlyToAccommodation()
    {
        var discount = new Discount(Guid.NewGuid(), Now);
        discount.Update("Постоянный гость", "VIP", DiscountType.Percent, 7.5m, null, null, 0, Now);
        var breakfast = Service("Завтрак", 450m, perNight: true);

        var quote = PriceCalculation.Calculate(Stay(1, 4), 3333.33m, null, [], discount, [(breakfast, 2)]);

        quote.AccommodationTotal.Should().Be(9999.99m);
        quote.DiscountAmount.Should().Be(750.00m); // 7,5 % от 9999,99 = 749,99925 → 750,00
        quote.ServicesTotal.Should().Be(2700m);    // 2 завтрака × 3 ночи × 450
        quote.Total.Should().Be(9999.99m - 750m + 2700m);
    }

    [Fact]
    public void AmountDiscount_NeverExceedsAccommodation()
    {
        var discount = new Discount(Guid.NewGuid(), Now);
        discount.Update("Сертификат", "GIFT", DiscountType.Amount, 50000m, null, null, 0, Now);

        var quote = PriceCalculation.Calculate(Stay(1, 2), 3000m, null, [], discount, []);

        quote.DiscountAmount.Should().Be(3000m);
        quote.Total.Should().Be(0m);
    }

    [Fact]
    public void OneTimeService_ChargedOnce()
    {
        var transfer = Service("Трансфер", 1500m, perNight: false);
        PriceCalculation.Calculate(Stay(1, 5), 1000m, null, [], null, [(transfer, 2)]).ServicesTotal.Should().Be(3000m);
    }

    [Theory]
    [InlineData(true, null, null, 0, 3, null)]
    [InlineData(false, null, null, 0, 3, "pricing.promo_inactive")]
    [InlineData(true, 5, null, 0, 3, "pricing.promo_out_of_period")]
    [InlineData(true, null, 2, 0, 3, "pricing.promo_out_of_period")]
    [InlineData(true, null, null, 4, 3, "pricing.promo_min_nights")]
    public void Discount_Applicability(bool active, int? fromDay, int? toDay, int minNights, int nights, string? expectedError)
    {
        var discount = new Discount(Guid.NewGuid(), Now);
        discount.Update("X", "CODE", DiscountType.Percent, 10m,
            fromDay is { } f ? new DateOnly(2026, 12, f) : null, toDay is { } t ? new DateOnly(2026, 12, t) : null, minNights, Now);
        discount.SetActive(active, Now);

        discount.CheckApplicable(Stay(3, 3 + nights))?.Code.Should().Be(expectedError);
    }

    [Fact]
    public void Hourly_UsesHourRates_GroupsEqualHours_AndChargesServicesOnce()
    {
        // 2026-12-04 — пятница: вечерний тариф «за час» на пятницу, ночные тарифы на часы не влияют
        var evening = Rate("Вечер пятницы", 4, 4, WeekDays.Friday, 700m, 1);
        evening.SetUnit(RateUnit.Hour);
        var nightly = Rate("Сезон", 1, 31, WeekDays.All, 9999m, 5);
        var water = Service("Вода", 100m, perNight: true);

        var quote = PriceCalculation.CalculateHourly(new DateTime(2026, 12, 3, 22, 0, 0), 4, 500m, null, [evening, nightly], null, [(water, 2)]);

        // 22:00 и 23:00 четверга — цена часа; 00:00 и 01:00 пятницы — тариф
        quote.Kind.Should().Be(PriceQuote.HourlyKind);
        quote.Hours.Should().HaveCount(2);
        quote.Hours![0].Should().BeEquivalentTo(new { Hours = 2, UnitPrice = 500m, Amount = 1000m, Source = PriceCalculation.HourPriceSource });
        quote.Hours[1].Should().BeEquivalentTo(new { Hours = 2, UnitPrice = 700m, Amount = 1400m, Source = "Тариф «Вечер пятницы»" });
        quote.AccommodationTotal.Should().Be(2400m);
        quote.ServicesTotal.Should().Be(200m);
        quote.Total.Should().Be(2600m);
    }

    [Fact]
    public void Hourly_RoomOverride_BeatsTypePrice_AndPromoChecksMinHoursOnly()
    {
        var quote = PriceCalculation.CalculateHourly(new DateTime(2026, 12, 3, 10, 0, 0), 3, 500m, 650m, [], null, []);
        quote.Total.Should().Be(1950m);
        quote.Hours!.Single().Source.Should().Be(PriceCalculation.RoomHourPriceSource);

        var discount = new Discount(Guid.NewGuid(), Now);
        discount.Update("Длительное", "LONG", DiscountType.Percent, 10m, null, null, 3, Now);
        discount.SetMinHours(4);
        discount.CheckApplicableHourly(new DateOnly(2026, 12, 3), 3)!.Code.Should().Be("pricing.promo_min_hours");
        discount.CheckApplicableHourly(new DateOnly(2026, 12, 3), 4).Should().BeNull("минимум ночей к почасовой брони не относится");
    }

    private static ExtraService Service(string name, decimal price, bool perNight)
    {
        var s = new ExtraService(Guid.NewGuid(), Now);
        s.Update(name, null, price, perNight, true, Now);
        return s;
    }
}

public class BookingDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Start = new(2026, 12, 1, 8, 0, 0, TimeSpan.Zero);

    private static StayPeriod Hourly(int hours, int buffer = 30) =>
        new(BookingKind.Hourly, new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 1), Start, Start.AddHours(hours), Start.AddHours(hours).AddMinutes(buffer), hours);

    private static Booking NewBooking(bool confirm, StayPeriod? stay = null) => Booking.Create(
        Guid.NewGuid(), "26-000001", BookingSource.Admin,
        stay ?? new StayPeriod(BookingKind.Nightly, new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 3), Start, Start.AddDays(2), Start.AddDays(2), 2),
        2, 0, new GuestContact("Гость", "+996555123456", null), null, Guid.NewGuid(), Guid.NewGuid(),
        new BookingPrice(6000m, 0m, 0m, 6000m, null, null, [], "{}"), confirm, Guid.NewGuid(), Now);

    [Theory]
    [InlineData(BookingStatus.Pending, BookingStatus.Confirmed, true)]
    [InlineData(BookingStatus.Pending, BookingStatus.Expired, true)]
    [InlineData(BookingStatus.Confirmed, BookingStatus.CheckedIn, true)]
    [InlineData(BookingStatus.Confirmed, BookingStatus.NoShow, true)]
    [InlineData(BookingStatus.CheckedIn, BookingStatus.CheckedOut, true)]
    [InlineData(BookingStatus.CheckedIn, BookingStatus.Cancelled, false)]
    [InlineData(BookingStatus.Cancelled, BookingStatus.Confirmed, false)]
    [InlineData(BookingStatus.CheckedOut, BookingStatus.CheckedIn, false)]
    [InlineData(BookingStatus.Pending, BookingStatus.CheckedIn, false)]
    public void StatusMachine_FollowsDocumentedTransitions(BookingStatus from, BookingStatus to, bool allowed)
    {
        BookingStatusMachine.CanTransition(from, to).Should().Be(allowed);
    }

    [Theory]
    [InlineData(BookingStatus.Pending, true)]
    [InlineData(BookingStatus.AwaitingPayment, true)]
    [InlineData(BookingStatus.Confirmed, true)]
    [InlineData(BookingStatus.CheckedIn, true)]
    [InlineData(BookingStatus.CheckedOut, false)]
    [InlineData(BookingStatus.Cancelled, false)]
    [InlineData(BookingStatus.NoShow, false)]
    [InlineData(BookingStatus.Expired, false)]
    public void Blocking_OnlyForActiveStatuses(BookingStatus status, bool blocking)
    {
        BookingStatusMachine.IsBlocking(status).Should().Be(blocking);
    }

    [Fact]
    public void Create_Pending_BlocksRoom_AndWritesHistory()
    {
        var booking = NewBooking(confirm: false);

        booking.Status.Should().Be(BookingStatus.Pending);
        booking.Rooms.Should().ContainSingle().Which.IsBlocking.Should().BeTrue();
        booking.History.Should().ContainSingle(h => h.EventType == "created");
        booking.ConfirmedAt.Should().BeNull();
    }

    [Fact]
    public void Cancel_RequiresReason_ReleasesRoom_AndForbidsFurtherConfirm()
    {
        var booking = NewBooking(confirm: true);

        booking.Cancel(" ", null, Now).Error!.Code.Should().Be("booking.reason_required");
        booking.Cancel("Гость передумал", null, Now).IsSuccess.Should().BeTrue();

        booking.Status.Should().Be(BookingStatus.Cancelled);
        booking.Rooms[0].IsBlocking.Should().BeFalse("отменённая бронь освобождает номер");
        booking.CancelReason.Should().Be("Гость передумал");
        booking.Confirm(null, Now).Error!.Code.Should().Be("booking.invalid_transition");
        booking.History.Select(h => h.EventType).Should().Equal("created", "status_changed");
    }

    [Fact]
    public void Extend_Hourly_MovesEnd_AddsAmount_AndWritesHistory()
    {
        var booking = NewBooking(confirm: true, Hourly(2));

        booking.Extend(Hourly(4), 2, 1000m, "{}", null, Now).IsSuccess.Should().BeTrue();

        booking.Hours.Should().Be(4);
        booking.EndsAt.Should().Be(Start.AddHours(4));
        booking.Rooms[0].BlockedUntil.Should().Be(Start.AddHours(4).AddMinutes(30));
        booking.ExtensionsAmount.Should().Be(1000m);
        booking.TotalAmount.Should().Be(7000m);
        booking.Extensions.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Quantity = 2, Amount = 1000m, PreviousEndsAt = Start.AddHours(2) });
        booking.History.Select(h => h.EventType).Should().Equal("created", "extended");
    }

    [Fact]
    public void Extend_IsForbidden_ForPendingOrCancelled_AndMustMoveEndForward()
    {
        NewBooking(confirm: false, Hourly(2)).Extend(Hourly(3), 1, 500m, "{}", null, Now).Error!.Code.Should().Be("booking.extend_not_allowed");

        var booking = NewBooking(confirm: true, Hourly(3));
        booking.Extend(Hourly(2), 1, 500m, "{}", null, Now).Error!.Code.Should().Be("booking.extend_invalid");
        booking.Cancel("Отмена", null, Now);
        booking.Extend(Hourly(4), 1, 500m, "{}", null, Now).Error!.Code.Should().Be("booking.extend_not_allowed");
    }

    [Theory]
    [InlineData(0, 2, false)]  // стык с учётом уборки: следующая бронь начинается в момент окончания уборки
    [InlineData(-1, 2, true)]  // начало раньше окончания уборки — пересечение
    [InlineData(-270, 1, false)] // бронь целиком раньше: 06:00–07:00 + уборка до 07:30, первая — с 08:00
    public void StayPeriod_Overlap_CountsCleaningBuffer(int startOffsetMinutes, int hours, bool overlaps)
    {
        var first = Hourly(2);
        var nextStart = first.BlockedUntil.AddMinutes(startOffsetMinutes);
        var next = new StayPeriod(BookingKind.Hourly, first.CheckIn, first.CheckOut, nextStart, nextStart.AddHours(hours), nextStart.AddHours(hours).AddMinutes(30), hours);

        first.Overlaps(next.StartsAt, next.BlockedUntil).Should().Be(overlaps);
    }
}
