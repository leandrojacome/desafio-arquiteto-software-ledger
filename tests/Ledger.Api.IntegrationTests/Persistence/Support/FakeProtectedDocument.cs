using System.Buffers.Binary;
using System.Security.Cryptography;
using Ledger.Application.Security;

namespace Ledger.Api.IntegrationTests.Persistence.Support;

internal static class FakeProtectedDocument
{
    private const byte FormatVersion = 1;
    private const int HeaderLength = 3;
    private const int BlobLength = 42;
    private const int BlindIndexLength = 32;

    public static ProtectedHolderDocument Create(ushort keyVersion)
    {
        var encrypted = RandomNumberGenerator.GetBytes(BlobLength);
        encrypted[0] = FormatVersion;
        BinaryPrimitives.WriteUInt16BigEndian(encrypted.AsSpan(1, HeaderLength - 1), keyVersion);

        return new ProtectedHolderDocument(encrypted, RandomNumberGenerator.GetBytes(BlindIndexLength), keyVersion);
    }
}
