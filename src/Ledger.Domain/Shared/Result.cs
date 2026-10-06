using System.Diagnostics.CodeAnalysis;

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

public sealed class Result<TValue> : Result
    where TValue : notnull
{
    internal Result(TValue value)
        : base(null)
    {
        Value = value;
    }

    internal Result(Error error)
        : base(error)
    {
    }

    public TValue Value
    {
        get => IsFailure
            ? throw new InvalidOperationException("A failed result does not carry a value.")
            : field ?? throw new InvalidOperationException("A successful result must carry a value.");
    }

    [SuppressMessage("Usage", "CA2225",
        Justification =
            "The named factories live on the non generic Result type to avoid static members on a generic type.")]
    public static implicit operator Result<TValue>(TValue value) => new(value);

    [SuppressMessage("Usage", "CA2225",
        Justification =
            "The named factories live on the non generic Result type to avoid static members on a generic type.")]
    public static implicit operator Result<TValue>(Error error) => new(error);
}
