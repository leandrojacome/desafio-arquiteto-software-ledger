using System.Collections.Frozen;
using Ledger.Domain.Accounts;
using Ledger.Domain.Shared;

namespace Ledger.Api.Validation;

internal static class BalanceQueryReader
{
    public const string AsOfName = "asOf";

    private static readonly FrozenSet<string> KnownNames = FrozenSet.ToFrozenSet([AsOfName], StringComparer.Ordinal);

    public static BalanceQueryRead Read(string? queryString)
    {
        var arguments = QueryStringReader.Read(queryString, KnownNames);

        if (arguments.UnknownFields.Count > 0)
        {
            return BalanceQueryRead.WithIssues(arguments.UnknownFields);
        }

        if (arguments.IsRepeated(AsOfName))
        {
            return BalanceQueryRead.Rejected(BalanceErrors.InvalidAsOf);
        }

        var text = arguments.ValueOf(AsOfName);

        if (text is null)
        {
            return BalanceQueryRead.Current();
        }

        return InstantParameterReader.TryRead(text, out var asOf)
            ? BalanceQueryRead.At(asOf)
            : BalanceQueryRead.Rejected(BalanceErrors.InvalidAsOf);
    }
}

internal sealed record BalanceQueryRead(DateTimeOffset? AsOf, IReadOnlyList<ValidationIssue> Issues, Error? Error)
{
    public bool IsValid => Error is null && Issues.Count == 0;

    public static BalanceQueryRead Current() => new(null, [], null);

    public static BalanceQueryRead At(DateTimeOffset asOf) => new(asOf, [], null);

    public static BalanceQueryRead WithIssues(IReadOnlyList<ValidationIssue> issues) => new(null, issues, null);

    public static BalanceQueryRead Rejected(Error error) => new(null, [], error);
}
