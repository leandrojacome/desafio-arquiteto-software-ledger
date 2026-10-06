using System.Text;

namespace Ledger.Infrastructure.Security;

internal sealed record RawKeySet(
    ushort Version,
    string EncryptionKey,
    string BlindIndexKey,
    string EncryptionKeyOrigin,
    string BlindIndexKeyOrigin)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Version = ").Append(Version);

        return true;
    }
}
