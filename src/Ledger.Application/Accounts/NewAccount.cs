using System.Text;
using Ledger.Application.Security;
using Ledger.Domain.Accounts;
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
