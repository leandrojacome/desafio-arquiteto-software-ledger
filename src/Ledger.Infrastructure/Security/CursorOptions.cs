using System.Security.Cryptography;
using Ledger.Application.Security;

namespace Ledger.Infrastructure.Security;

internal sealed class CursorOptions
{
    public const string SectionName = "Security:Cursor";
    public const string SigningKeyName = $"{SectionName}:SigningKey";
    public const int SigningKeySize = 32;

    [Sensitive] public string? SigningKey { get; init; }

    internal byte[]? DecodeSigningKey()
    {
        if (string.IsNullOrWhiteSpace(SigningKey))
        {
            return null;
        }

        var buffer = new byte[(SigningKey.Length * 3 / 4) + 3];

        if (!Convert.TryFromBase64String(SigningKey.Trim(), buffer, out var written) || written != SigningKeySize)
        {
            return null;
        }

        var key = buffer.AsSpan(0, written).ToArray();
        CryptographicOperations.ZeroMemory(buffer);

        return key;
    }
}
