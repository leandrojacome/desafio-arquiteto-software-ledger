using System.Text;
using Ledger.Domain.Accounts;
using Ledger.Domain.Shared;

namespace Ledger.Api.Validation;

internal sealed record CreateAccountInput(HolderDocument HolderDocument, string Currency, Money OverdraftLimit)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Currency = ").Append(Currency);

        return true;
    }
}
