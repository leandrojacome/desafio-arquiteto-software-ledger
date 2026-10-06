using System.Text;
using Ledger.Domain.Accounts;

namespace Ledger.Application.Integrity;

public sealed record AccountIntegrityReport(
    AccountId AccountId,
    decimal StoredBalance,
    decimal EntriesSum,
    long EntryCount,
    IReadOnlyList<IntegrityFinding> Findings)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder
            .Append("AccountId = ").Append(AccountId)
            .Append(", EntryCount = ").Append(EntryCount)
            .Append(", Findings = ").Append(Findings.Count);

        return true;
    }
}
