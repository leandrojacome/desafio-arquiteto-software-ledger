using System.Text;
using Ledger.Application.Security;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;

namespace Ledger.Application.Accounts;

public sealed record NewAccount(
    AccountId Id,
    string Currency,
    Money OverdraftLimit,
    ProtectedHolderDocument Document)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Id = ").Append(Id).Append(", Currency = ").Append(Currency);

        return true;
    }
}

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
