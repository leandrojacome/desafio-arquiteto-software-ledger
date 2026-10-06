using System.Collections.Frozen;
using Ledger.Domain.Accounts;

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
