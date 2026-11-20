namespace Hms.SharedKernel;

/// <summary>Категория ошибки — определяет HTTP-статус при преобразовании в ProblemDetails.</summary>
public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    Unauthorized,
    BusinessRule,
}

/// <summary>Доменная ошибка с машиночитаемым кодом (например, <c>booking.room_unavailable</c>).</summary>
public sealed record Error
{
    public Error(string code, string message, ErrorType type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Code = code;
        Message = message;
        Type = type;
    }

    public string Code { get; }

    public string Message { get; }

    public ErrorType Type { get; }

    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);

    public static Error BusinessRule(string code, string message) => new(code, message, ErrorType.BusinessRule);
}
