using System.Diagnostics.CodeAnalysis;

namespace Hms.SharedKernel;

/// <summary>Результат операции без значения: успех или доменная ошибка.</summary>
public class Result
{
    protected Result(Error? error)
    {
        Error = error;
    }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsFailure => !IsSuccess;

    public Error? Error { get; }

    public static Result Success() => new(null);

    public static Result Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result(error);
    }

    public static Result<T> Success<T>(T value) => Result<T>.Success(value);

    public static Result<T> Failure<T>(Error error) => Result<T>.Failure(error);
}

/// <summary>Результат операции со значением.</summary>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T? value, Error? error)
        : base(error)
    {
        _value = value;
    }

    /// <summary>Значение успешного результата. Обращение при ошибке — ошибка программиста.</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Нельзя получить значение неуспешного результата ({Error.Code}).");

    public static Result<T> Success(T value) => new(value, null);

    public static new Result<T> Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<T>(default, error);
    }

#pragma warning disable CA2225 // Альтернатива оператору — метод Failure
    public static implicit operator Result<T>(Error error) => Failure(error);
#pragma warning restore CA2225
}
