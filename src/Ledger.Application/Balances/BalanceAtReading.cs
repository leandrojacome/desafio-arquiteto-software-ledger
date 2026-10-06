using System.Text;
using Ledger.Domain.Entries;

namespace Ledger.Application.Balances;

public sealed record BalanceAtReading(
    string Currency,
    decimal OverdraftLimit,
    decimal? BalanceAfter,
    EntryId? LastEntryId,
    DateTimeOffset DatabaseNow)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Currency = ").Append(Currency);

        return true;
    }
}
