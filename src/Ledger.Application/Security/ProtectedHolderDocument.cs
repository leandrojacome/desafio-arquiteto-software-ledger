using System.Text;

namespace Ledger.Application.Security;

public sealed record ProtectedHolderDocument(
    [property: Sensitive] ReadOnlyMemory<byte> Encrypted,
    [property: Sensitive] ReadOnlyMemory<byte> BlindIndex,
    int KeyVersion)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("KeyVersion = ").Append(KeyVersion);

        return true;
    }
}
