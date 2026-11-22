using Hms.Modules.Reporting.Application;
using Hms.Modules.Reporting.Infrastructure;

namespace Hms.UnitTests.Reporting;

/// <summary>Подпись периода в Excel/PDF и проверка часов.</summary>
public class ReportPeriodTests
{
    private static ReportTable Table(DateOnly from, DateOnly to, int? fromHour, int? toHour) =>
        new("financial", "Финансовый отчёт", from, to, [], [], [], [], null, null, fromHour, toHour);

    private static readonly DateOnly Day = new(2026, 11, 22);

    [Fact]
    public void Period_WithAndWithoutHours()
    {
        ReportExporter.Period(Table(Day, Day, null, null)).Should().Be("22.11.2026");
        ReportExporter.Period(Table(Day, Day, 6, 22)).Should().Be("22.11.2026 06:00–22:00");
        ReportExporter.Period(Table(Day.AddDays(-2), Day, 6, null)).Should().Be("20.11.2026 06:00 — 22.11.2026 24:00");
    }

    [Fact]
    public void Hours_Validation()
    {
        ReportData.HoursValid(Day, Day, 6, 22).Should().BeTrue();
        ReportData.HoursValid(Day, Day, 22, 6).Should().BeFalse("в один день окончание позже начала");
        ReportData.HoursValid(Day.AddDays(-1), Day, 22, 6).Should().BeTrue("с 22:00 вчера до 06:00 сегодня");
        ReportData.HoursValid(Day, Day, 24, null).Should().BeFalse();
        ReportData.HoursValid(Day, Day, null, 0).Should().BeFalse();
    }
}
