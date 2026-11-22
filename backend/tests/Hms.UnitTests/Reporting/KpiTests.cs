using Hms.Modules.Reporting.Domain;

namespace Hms.UnitTests.Reporting;

/// <summary>Показатели на эталонных данных: значения посчитаны вручную (критерий Sprint 9 — сверка KPI).</summary>
public class KpiTests
{
    private static readonly DateOnly Day1 = new(2026, 12, 1);

    // Для простоты — гостиница в UTC: сутки = [00:00, 24:00).
    private static PeriodDay Day(int offset) =>
        new(Day1.AddDays(offset), new DateTimeOffset(Day1.AddDays(offset).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            new DateTimeOffset(Day1.AddDays(offset + 1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

    private static DateTimeOffset At(int day, int hour) => Day(day).Start.AddHours(hour);

    private static readonly StayInput Hourly = new(true, StayStatus.CheckedOut, At(0, 10), At(0, 14), Day1, Day1, 2000m, 2000m, 0m, null);

    private static readonly StayInput Nightly = new(false, StayStatus.CheckedIn, At(0, 14), At(2, 12), Day1, Day1.AddDays(2), 6000m, 6000m, 0m, null);

    private static readonly StayInput CancelledWithPenalty = new(true, StayStatus.Cancelled, At(1, 18), At(1, 20), Day1.AddDays(1), Day1.AddDays(1), 1000m, 1000m, 500m, At(1, 9));

    private static readonly StayInput Expired = new(true, StayStatus.Expired, At(1, 10), At(1, 12), Day1.AddDays(1), Day1.AddDays(1), 1000m, 1000m, 0m, null);

    private static KpiResult TwoDays() => Kpi.Calculate(
        [Day(0), Day(1)],
        availableRooms: 2,
        [Hourly, Nightly, CancelledWithPenalty, Expired],
        [new MoneyInput(At(0, 12), 3000m), new MoneyInput(At(1, 9), -500m), new MoneyInput(At(5, 9), 9999m)],
        [new MoneyInput(At(1, 15), 200m)],
        [new ExpenseInput(Day1, 600m), new ExpenseInput(Day1.AddDays(1), 400m), new ExpenseInput(Day1.AddDays(9), 777m)]);

    [Fact]
    public void Money_SeparatesAccruedCashRefundsExpensesAndProfit()
    {
        var k = TwoDays();

        k.AccruedRevenue.Should().Be(8500m, "2000 почасовая + 6000 две ночи + 500 штраф; истёкшая не в счёт");
        k.CashIn.Should().Be(2500m, "3000 оплата − 500 аннулирование; платёж вне периода не в счёт");
        k.RefundsPaid.Should().Be(200m);
        k.NetRevenue.Should().Be(2300m);
        k.Expenses.Should().Be(1000m);
        k.NetProfit.Should().Be(1300m);
        k.AccruedProfit.Should().Be(7500m);
    }

    [Fact]
    public void Occupancy_And_HourlyKpis()
    {
        var k = TwoDays();

        // Занято: 4 ч почасовой + 34 ч посуточной внутри периода (с 14:00 первого дня до конца второго) = 38 из 2 × 48.
        k.Occupancy.Should().Be(0.3958m);
        k.HoursSold.Should().Be(4m);
        k.AvgHourPrice.Should().Be(500m);
        k.RevenuePerRoomHour.Should().Be(83.33m, "8000 за проживание / 96 номеро-часов");
        k.AvgStayHours.Should().Be(4m);
    }

    [Fact]
    public void Nightly_AdrRevPar_OnlyFromNightlyStays()
    {
        var k = TwoDays();

        k.RoomNightsSold.Should().Be(2m);
        k.Adr.Should().Be(3000m);
        k.RevPar.Should().Be(1500m, "6000 / (2 номера × 2 суток)");
    }

    [Fact]
    public void Bookings_CountedByStartInPeriod()
    {
        var k = TwoDays();

        k.TotalBookings.Should().Be(3, "истёкшая бронь не считается");
        k.CancelledBookings.Should().Be(1);
        k.CancellationRate.Should().Be(0.3333m);
        k.Turnover.Should().Be(0.5m, "2 состоявшиеся брони / (2 номера × 2 дня)");
    }

    [Fact]
    public void NightlyStay_SplitsRevenueByNights_AcrossPeriods()
    {
        var first = Kpi.Calculate([Day(0)], 2, [Nightly], [], [], []);
        var second = Kpi.Calculate([Day(1)], 2, [Nightly], [], [], []);

        first.AccruedRevenue.Should().Be(3000m);
        second.AccruedRevenue.Should().Be(3000m);
        first.Adr.Should().Be(3000m);
    }

    [Fact]
    public void Daily_SumsMatchPeriod()
    {
        var days = new[] { Day(0), Day(1) };
        var points = Kpi.Daily(days, 2, [Hourly, Nightly, CancelledWithPenalty], [new MoneyInput(At(0, 12), 3000m)], [new ExpenseInput(Day1, 600m)]);

        points.Should().HaveCount(2);
        points.Sum(p => p.AccruedRevenue).Should().Be(8500m);
        points[0].AccruedRevenue.Should().Be(5000m, "2000 почасовая + 3000 первая ночь");
        points[1].AccruedRevenue.Should().Be(3500m, "3000 вторая ночь + 500 штраф");
        points[0].Arrivals.Should().Be(2);
        points[0].CashIn.Should().Be(3000m);
    }

    [Fact]
    public void NoRooms_DoesNotDivideByZero()
    {
        var k = Kpi.Calculate([Day(0)], 0, [], [], [], []);

        k.Occupancy.Should().Be(0m);
        k.Adr.Should().BeNull();
        k.AvgHourPrice.Should().BeNull();
    }

    [Fact]
    public void PartOfDay_CountsOnlyHoursInsideWindow()
    {
        // Окно 12:00–22:00 первого дня: почасовая 10:00–14:00 попадает наполовину.
        var window = Day(0) with { Start = At(0, 12), End = At(0, 22) };
        var k = Kpi.Calculate([window], 1, [Hourly], [new MoneyInput(At(0, 11), 500m), new MoneyInput(At(0, 13), 700m)], [], []);

        k.AccruedRevenue.Should().Be(1000m);
        k.HoursSold.Should().Be(2m);
        k.CashIn.Should().Be(700m, "платёж в 11:00 — до начала окна");
        k.Occupancy.Should().Be(0.2m, "2 ч из 10");
    }
}
