using System.Security.Cryptography;
using System.Text;

namespace Ledger.Infrastructure.Security;

internal static class HmacBlindIndex
{
    public const int Size = 32;

    private static ReadOnlySpan<byte> DomainPrefix => "holder-document:"u8;

    public static byte[] Compute(ReadOnlySpan<byte> key, ReadOnlySpan<byte> normalizedDocument)
    {
        Span<byte> input = stackalloc byte[DomainPrefix.Length + normalizedDocument.Length];
        DomainPrefix.CopyTo(input);
        normalizedDocument.CopyTo(input[DomainPrefix.Length..]);

        var index = new byte[Size];
        HMACSHA256.HashData(key, input, index);

        return index;
    }

    public static byte[] Compute(ReadOnlySpan<byte> key, string normalizedDocument)
    {
        Span<byte> document = stackalloc byte[Encoding.UTF8.GetMaxByteCount(normalizedDocument.Length)];
        var written = Encoding.UTF8.GetBytes(normalizedDocument, document);

        try
        {
            return Compute(key, document[..written]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(document);
        }
    }
}
