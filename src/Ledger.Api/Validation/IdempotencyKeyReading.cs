using Ledger.Domain.Entries;

namespace Ledger.Api.Validation;

internal sealed class IdempotencyKeyReading
{
    private readonly IdempotencyKey? _key;

    private IdempotencyKeyReading(IdempotencyKey? key, ValidationIssue? issue)
    {
        _key = key;
        Issue = issue;
    }

    public static IdempotencyKeyReading Missing { get; } = new(null, null);

    public ValidationIssue? Issue { get; }

    public bool IsMissing => _key is null && Issue is null;

    public IdempotencyKey Key => _key ?? throw new InvalidOperationException("No valid Idempotency-Key was read.");

    public static IdempotencyKeyReading Accepted(IdempotencyKey key) => new(key, null);

    public static IdempotencyKeyReading Rejected(ValidationIssue issue) => new(null, issue);
}
