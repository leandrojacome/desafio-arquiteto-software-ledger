namespace Ledger.Domain.Shared;

public class Result
{
    private readonly Error? _error;

    private protected Result(Error? error)
    {
        _error = error;
    }

    public Error Error =>
        _error ?? throw new InvalidOperationException("A successful result does not carry an error.");

    public bool IsSuccess => _error is null;

    public bool IsFailure => _error is not null;

    public static Result Success() => new(null);

    public static Result Failure(Error error) => new(error);

    public static Result<TValue> Success<TValue>(TValue value)
        where TValue : notnull => new(value);

    public static Result<TValue> Failure<TValue>(Error error)
        where TValue : notnull => new(error);
}
