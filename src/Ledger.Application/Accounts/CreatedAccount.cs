using System.Text;
using Ledger.Domain.Accounts;
using Ledger.Domain.Shared;

namespace Ledger.Application.Accounts;

public sealed record CreatedAccount(
    AccountId AccountId,
    string Currency,
    Money OverdraftLimit,
    string HolderDocumentMasked,
    DateTimeOffset CreatedAt,
    bool IsReplay = false)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("AccountId = ").Append(AccountId).Append(", Currency = ").Append(Currency);

        return true;
    }
}
