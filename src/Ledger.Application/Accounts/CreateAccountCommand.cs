using System.Text;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;

namespace Ledger.Application.Accounts;

public sealed record CreateAccountCommand(
    HolderDocument HolderDocument,
    string Currency,
    Money OverdraftLimit,
    string ClientId,
    string CorrelationId,
    IdempotencyKey? IdempotencyKey = null)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder
            .Append("Currency = ").Append(Currency)
            .Append(", ClientId = ").Append(ClientId)
            .Append(", CorrelationId = ").Append(CorrelationId);

        return true;
    }
}
