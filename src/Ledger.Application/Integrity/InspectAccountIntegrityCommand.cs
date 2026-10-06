using System.Text;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;

namespace Ledger.Application.Integrity;

public sealed record InspectAccountIntegrityCommand(AccountId AccountId);

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

public sealed record IntegrityFinding(
    IntegrityCheck Check,
    AccountId AccountId,
    EntryId? EntryId,
    long? AccountVersion,
    string Expected,
    string Found)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Check = ").Append(Check).Append(", AccountId = ").Append(AccountId);

        if (EntryId is { } entryId)
        {
            builder.Append(", EntryId = ").Append(entryId);
        }

        return true;
    }
}
