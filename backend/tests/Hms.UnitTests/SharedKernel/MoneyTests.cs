using Hms.SharedKernel;

namespace Hms.UnitTests.SharedKernel;

public class MoneyTests
{
    [Fact]
    public void DefaultCurrency_IsKgs()
    {
        Money.Kgs(100).Currency.Should().Be("KGS");
        new Money(1m).Currency.Should().Be("KGS");
    }

    [Theory]
    [InlineData("10.005", "10.01")]
    [InlineData("10.004", "10.00")]
    [InlineData("-10.005", "-10.01")]
    [InlineData("0.125", "0.13")]
    public void Amount_RoundedToTwoDecimals_AwayFromZero(string input, string expected)
    {
        Money.Kgs(decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture)).Amount
            .Should().Be(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Arithmetic_IsExactWithoutFloatingPointErrors()
    {
        var sum = Money.Kgs(0.1m) + Money.Kgs(0.2m);

        sum.Should().Be(Money.Kgs(0.3m));
        (Money.Kgs(3500m) * 3).Should().Be(Money.Kgs(10500m));
        (Money.Kgs(100m) - Money.Kgs(150m)).IsNegative.Should().BeTrue();
    }

    [Fact]
    public void Multiply_ByPercentage_RoundsResult()
    {
        // 30% предоплаты от 3333.33
        (Money.Kgs(3333.33m) * 0.3m).Should().Be(Money.Kgs(1000.00m));
    }

    [Fact]
    public void Operations_DifferentCurrencies_Throw()
    {
        var kgs = Money.Kgs(100);
        var usd = new Money(100, "USD");

        ((Func<Money>)(() => kgs + usd)).Should().Throw<InvalidOperationException>();
        ((Func<bool>)(() => kgs > usd)).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("kgs")]
    [InlineData("KG")]
    [InlineData("KGS1")]
    [InlineData(" ")]
    public void Constructor_InvalidCurrency_Throws(string currency)
    {
        ((Func<Money>)(() => new Money(1, currency))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_ExceedsNumeric14_2_Throws()
    {
        ((Func<Money>)(() => Money.Kgs(1_000_000_000_000m))).Should().Throw<ArgumentOutOfRangeException>();
        Money.Kgs(Money.MaxAbsoluteAmount).Amount.Should().Be(Money.MaxAbsoluteAmount);
    }

    [Fact]
    public void Comparison_And_ToString()
    {
        (Money.Kgs(10) < Money.Kgs(20)).Should().BeTrue();
        (Money.Kgs(20) >= Money.Kgs(20)).Should().BeTrue();
        Money.Kgs(1234.5m).ToString().Should().Be("1234.50 KGS");
    }
}
