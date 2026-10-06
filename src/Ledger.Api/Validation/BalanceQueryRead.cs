using Ledger.Domain.Shared;

namespace Ledger.Api.Validation;

internal sealed record BalanceQueryRead(DateTimeOffset? AsOf, IReadOnlyList<ValidationIssue> Issues, Error? Error)
{
    public bool IsValid => Error is null && Issues.Count == 0;

    public static BalanceQueryRead Current() => new(null, [], null);

    public static BalanceQueryRead At(DateTimeOffset asOf) => new(asOf, [], null);

    public static BalanceQueryRead WithIssues(IReadOnlyList<ValidationIssue> issues) => new(null, issues, null);

    public static BalanceQueryRead Rejected(Error error) => new(null, [], error);
}
