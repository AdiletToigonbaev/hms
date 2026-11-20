using Hms.SharedKernel;

namespace Hms.Api.Infrastructure;

/// <summary>Преобразование доменных ошибок в HTTP-ответы ProblemDetails с кодом домена.</summary>
internal static class ResultHttpExtensions
{
    public static int ToStatusCode(this ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.BusinessRule => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status500InternalServerError,
    };

    public static IResult ToProblem(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return Results.Problem(
            statusCode: error.Type.ToStatusCode(),
            title: error.Message,
            extensions: new Dictionary<string, object?> { [ProblemDetailsSetup.CodeKey] = error.Code });
    }

    public static IResult ToHttpResult(this Result result, Func<IResult>? onSuccess = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess ? (onSuccess?.Invoke() ?? Results.NoContent()) : result.Error.ToProblem();
    }

    public static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult>? onSuccess = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess ? (onSuccess?.Invoke(result.Value) ?? Results.Ok(result.Value)) : result.Error.ToProblem();
    }
}
