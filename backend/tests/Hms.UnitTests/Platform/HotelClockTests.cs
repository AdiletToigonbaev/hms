using Hms.Platform.Time;

namespace Hms.UnitTests.Platform;

public class HotelClockTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static HotelClock At(string utcIso) =>
        new(new FixedTimeProvider(DateTimeOffset.Parse(utcIso, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Today_UsesBishkekDate_NotUtcDate()
    {
        // 2026-11-30 19:30 UTC = 2026-12-01 01:30 в Бишкеке (UTC+6)
        var clock = At("2026-11-30T19:30:00Z");

        clock.Today.Should().Be(new DateOnly(2026, 12, 1));
        clock.LocalNow.Offset.Should().Be(TimeSpan.FromHours(6));
    }

    [Fact]
    public void Today_BeforeLocalMidnight_IsPreviousDay()
    {
        // 17:59 UTC = 23:59 в Бишкеке
        At("2026-11-30T17:59:00Z").Today.Should().Be(new DateOnly(2026, 11, 30));
    }

    [Theory]
    [InlineData("2026-01-15")]
    [InlineData("2026-07-15")]
    public void ToUtc_ConvertsHotelLocalTime_NoDaylightSaving(string date)
    {
        var clock = At("2026-01-01T00:00:00Z");

        // Заезд в 14:00 по Бишкеку = 08:00 UTC круглый год (переходов на летнее время нет)
        var utc = clock.ToUtc(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), new TimeOnly(14, 0));

        utc.Offset.Should().Be(TimeSpan.Zero);
        utc.Hour.Should().Be(8);
    }

    [Fact]
    public void TimeZone_IsAsiaBishkek()
    {
        At("2026-01-01T00:00:00Z").TimeZone.BaseUtcOffset.Should().Be(TimeSpan.FromHours(6));
    }
}
