namespace Ledger.Api.Validation;

internal sealed class ReadResult<TValue>
    where TValue : class
{
    private readonly TValue? _value;

    internal ReadResult(TValue? value, IReadOnlyList<ValidationIssue> issues)
    {
        _value = value;
        Issues = issues;
    }

    public bool IsValid => Issues.Count == 0 && _value is not null;

    public IReadOnlyList<ValidationIssue> Issues { get; }

    public TValue Value => IsValid
        ? _value ?? throw new InvalidOperationException("A valid read result must carry a value.")
        : throw new InvalidOperationException("An invalid read result does not carry a value.");
}

internal static class ReadResult
{
    public static ReadResult<TValue> Valid<TValue>(TValue value)
        where TValue : class => new(value, []);

    public static ReadResult<TValue> Invalid<TValue>(IReadOnlyList<ValidationIssue> issues)
        where TValue : class => new(null, issues);
}
