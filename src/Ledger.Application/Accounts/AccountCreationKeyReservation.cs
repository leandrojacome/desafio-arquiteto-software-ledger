using System.Text;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;

namespace Ledger.Application.Accounts;

public sealed record AccountCreationKeyReservation(
    string ClientId,
    IdempotencyKey Key,
    ReadOnlyMemory<byte> RequestHash,
    int HashVersion,
    AccountId AccountId)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder
            .Append("ClientId = ").Append(ClientId)
            .Append(", Key = ").Append(Key)
            .Append(", AccountId = ").Append(AccountId);

        return true;
    }
}
