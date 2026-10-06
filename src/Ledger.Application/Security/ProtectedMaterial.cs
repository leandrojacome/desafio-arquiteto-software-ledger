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
