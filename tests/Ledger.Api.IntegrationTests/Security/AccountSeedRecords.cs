using System.Text;
using Ledger.Domain.Accounts;

namespace Ledger.Api.IntegrationTests.Security;

internal sealed record SeededAccount(AccountId Id, HolderDocument Document);

internal sealed record StoredDocument(byte[]? Encrypted, byte[]? BlindIndex, int? KeyVersion)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("KeyVersion = ").Append(KeyVersion);

        return true;
    }
}
