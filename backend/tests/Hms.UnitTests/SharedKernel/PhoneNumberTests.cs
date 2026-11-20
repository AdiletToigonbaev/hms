using Hms.SharedKernel;

namespace Hms.UnitTests.SharedKernel;

public class PhoneNumberTests
{
    [Theory]
    [InlineData("+996555123456", "+996555123456")]
    [InlineData("+996 (555) 123-456", "+996555123456")]
    [InlineData("996555123456", "+996555123456")]
    [InlineData("0555 12-34-56", "+996555123456")]
    [InlineData("555123456", "+996555123456")]
    [InlineData("  0700 111 222 ", "+996700111222")]
    [InlineData("+7 701 123 4567", "+77011234567")]
    public void Parse_ValidFormats_NormalizesToE164(string input, string expected)
    {
        var result = PhoneNumber.Parse(input);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("+996 555 12")]
    [InlineData("+99655512345678")]
    [InlineData("12345")]
    [InlineData("+0555123456")]
    [InlineData("+996555123456; DROP TABLE users")]
    public void Parse_InvalidInput_ReturnsValidationError(string? input)
    {
        var result = PhoneNumber.Parse(input);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("phone.invalid");
    }
}
