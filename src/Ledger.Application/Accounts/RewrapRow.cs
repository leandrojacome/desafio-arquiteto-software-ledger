using System.Text;
using Ledger.Application.Security;
using Ledger.Domain.Accounts;

namespace Ledger.Application.Accounts;

public sealed record RewrapRow(AccountId Id, [property: Sensitive] ReadOnlyMemory<byte> Encrypted, int KeyVersion)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Id = ").Append(Id).Append(", KeyVersion = ").Append(KeyVersion);

        return true;
    }
}
