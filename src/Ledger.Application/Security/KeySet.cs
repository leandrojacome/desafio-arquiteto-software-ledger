using System.Text;

namespace Ledger.Application.Security;

public sealed record KeySet(
    ushort Version,
    [property: Sensitive] ReadOnlyMemory<byte> EncryptionKey,
    [property: Sensitive] ReadOnlyMemory<byte> BlindIndexKey)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Version = ").Append(Version);

        return true;
    }
}
