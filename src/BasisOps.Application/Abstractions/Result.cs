namespace BasisOps.Application.Abstractions;

public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
}

/// <summary>An expected failure. The Api maps <see cref="Kind"/> to a ProblemDetails status code.</summary>
public sealed record Error(ErrorKind Kind, string Code, string Message, IReadOnlyDictionary<string, string[]>? ValidationErrors = null)
{
    public static Error Validation(IReadOnlyDictionary<string, string[]> errors) =>
        new(ErrorKind.Validation, "validation_failed", "One or more validation errors occurred.", errors);

    public static Error NotFound(string code, string message) => new(ErrorKind.NotFound, code, message);

    public static Error Conflict(string code, string message) => new(ErrorKind.Conflict, code, message);

    public static Error Forbidden(string code, string message) => new(ErrorKind.Forbidden, code, message);
}

public sealed class Result<T>
{
    private readonly T? _value;

    private Result(T value)
    {
        _value = value;
        IsSuccess = true;
    }

    private Result(Error error)
    {
        Error = error;
    }

    public bool IsSuccess { get; }

    public Error? Error { get; }

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException($"Result has no value: {Error!.Code}.");

    public static implicit operator Result<T>(T value) => new(value);

    public static implicit operator Result<T>(Error error) => new(error);
}
