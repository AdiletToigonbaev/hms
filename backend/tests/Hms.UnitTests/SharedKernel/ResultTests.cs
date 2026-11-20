using Hms.SharedKernel;

namespace Hms.UnitTests.SharedKernel;

public class ResultTests
{
    private static readonly Error SampleError = Error.Conflict("booking.room_unavailable", "Номер занят.");

    [Fact]
    public void Success_HasNoError()
    {
        var result = Result.Success();

        result.IsSuccess.Should().BeTrue();
        result.Error.Should().BeNull();
    }

    [Fact]
    public void Failure_CarriesError()
    {
        var result = Result.Failure(SampleError);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SampleError);
    }

    [Fact]
    public void GenericSuccess_ExposesValue()
    {
        Result.Success(42).Value.Should().Be(42);
    }

    [Fact]
    public void GenericFailure_AccessingValue_Throws()
    {
        Result<int> result = SampleError;

        result.IsFailure.Should().BeTrue();
        ((Func<int>)(() => result.Value)).Should().Throw<InvalidOperationException>()
            .WithMessage("*booking.room_unavailable*");
    }

    [Theory]
    [InlineData("", "msg")]
    [InlineData("code", " ")]
    public void Error_RequiresCodeAndMessage(string code, string message)
    {
        ((Func<Error>)(() => new Error(code, message, ErrorType.Validation))).Should().Throw<ArgumentException>();
    }
}
