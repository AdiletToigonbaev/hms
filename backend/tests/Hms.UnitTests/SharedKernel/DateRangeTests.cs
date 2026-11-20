using Hms.SharedKernel;

namespace Hms.UnitTests.SharedKernel;

public class DateRangeTests
{
    private static DateOnly D(int month, int day) => new(2026, month, day);

    [Fact]
    public void Constructor_EndNotAfterStart_Throws()
    {
        var sameDay = () => new DateRange(D(12, 1), D(12, 1));
        var reversed = () => new DateRange(D(12, 2), D(12, 1));

        sameDay.Should().Throw<ArgumentException>();
        reversed.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_InvalidRange_ReturnsValidationError()
    {
        var result = DateRange.Create(D(12, 5), D(12, 5));

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("date_range.invalid");
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void Nights_CountsHalfOpenInterval()
    {
        new DateRange(D(12, 1), D(12, 4)).Nights.Should().Be(3);
        new DateRange(D(12, 31), new DateOnly(2027, 1, 1)).Nights.Should().Be(1);
    }

    [Fact]
    public void Overlaps_CheckoutDayEqualsNextCheckin_IsNotOverlap()
    {
        // Гость А выезжает 5-го, гость Б заезжает 5-го — допустимо: интервал [CheckIn, CheckOut).
        var a = new DateRange(D(12, 1), D(12, 5));
        var b = new DateRange(D(12, 5), D(12, 8));

        a.Overlaps(b).Should().BeFalse();
        b.Overlaps(a).Should().BeFalse();
    }

    [Theory]
    [InlineData(1, 5, 4, 8)]   // частичное пересечение
    [InlineData(1, 10, 3, 4)]  // вложенный интервал
    [InlineData(3, 4, 1, 10)]  // охватывающий интервал
    [InlineData(1, 5, 1, 5)]   // совпадение
    public void Overlaps_IntersectingRanges_IsOverlap(int s1, int e1, int s2, int e2)
    {
        var first = new DateRange(D(12, s1), D(12, e1));
        var second = new DateRange(D(12, s2), D(12, e2));

        first.Overlaps(second).Should().BeTrue();
        second.Overlaps(first).Should().BeTrue();
    }

    [Fact]
    public void Contains_IncludesCheckInExcludesCheckOut()
    {
        var range = new DateRange(D(12, 1), D(12, 3));

        range.Contains(D(12, 1)).Should().BeTrue();
        range.Contains(D(12, 2)).Should().BeTrue();
        range.Contains(D(12, 3)).Should().BeFalse();
        range.Contains(D(11, 30)).Should().BeFalse();
    }

    [Fact]
    public void EachNight_EnumeratesAllNightsExceptCheckOut()
    {
        new DateRange(D(12, 30), new DateOnly(2027, 1, 2)).EachNight().Should()
            .Equal(D(12, 30), D(12, 31), new DateOnly(2027, 1, 1));
    }
}
