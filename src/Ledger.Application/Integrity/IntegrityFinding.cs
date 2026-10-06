using System.Text;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;

namespace Ledger.Application.Integrity;

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
