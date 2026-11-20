using Hms.Api.Infrastructure;
using Hms.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Hms.ApiTests;

public class ResultMappingTests
{
    [Theory]
    [InlineData(ErrorType.Validation, 400)]
    [InlineData(ErrorType.Unauthorized, 401)]
    [InlineData(ErrorType.Forbidden, 403)]
    [InlineData(ErrorType.NotFound, 404)]
    [InlineData(ErrorType.Conflict, 409)]
    [InlineData(ErrorType.BusinessRule, 422)]
    public void ErrorType_MapsToHttpStatus(ErrorType type, int expected)
    {
        type.ToStatusCode().Should().Be(expected);
    }

    [Fact]
    public void ToProblem_IncludesDomainCodeAndStatus()
    {
        var result = Error.Conflict("booking.room_unavailable", "Номер занят на выбранные даты.").ToProblem();

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject.ProblemDetails;
        problem.Status.Should().Be(StatusCodes.Status409Conflict);
        problem.Title.Should().Be("Номер занят на выбранные даты.");
        problem.Extensions["code"].Should().Be("booking.room_unavailable");
    }

    [Fact]
    public void ToHttpResult_Success_UsesSuccessFactory()
    {
        Result.Success(5).ToHttpResult().Should().BeOfType<Ok<int>>().Which.Value.Should().Be(5);
        Result.Success().ToHttpResult().Should().BeOfType<NoContent>();
    }

    [Fact]
    public void ToHttpResult_Failure_ReturnsProblem()
    {
        Result.Failure<int>(Error.NotFound("booking.not_found", "Бронь не найдена.")).ToHttpResult()
            .Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(404);
    }

    [Fact]
    public void ProblemDetailsType_IsMvcProblemDetails()
    {
        // Защита от случайной подмены формата: ответы ошибок — стандартный ProblemDetails (RFC 9457).
        Error.Validation("validation.failed", "Ошибка валидации.").ToProblem()
            .Should().BeOfType<ProblemHttpResult>().Which.ProblemDetails.Should().BeAssignableTo<ProblemDetails>();
    }
}
