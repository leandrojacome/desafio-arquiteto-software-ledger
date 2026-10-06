using System.Text;
using Ledger.Domain.Accounts;

namespace Ledger.Application.Accounts;

public sealed record AccountCreationKeyRecord(
    ReadOnlyMemory<byte> RequestHash,
    int HashVersion,
    AccountId AccountId,
    DateTimeOffset CreatedAt)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("AccountId = ").Append(AccountId).Append(", HashVersion = ").Append(HashVersion);

        return true;
    }
}
