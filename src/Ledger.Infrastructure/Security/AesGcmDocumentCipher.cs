using System.Buffers.Binary;
using System.Security.Cryptography;
using Ledger.Application.Security;
using Ledger.Domain.Accounts;
using Ledger.Domain.Shared;

namespace Ledger.Infrastructure.Security;

internal delegate void NonceFiller(Span<byte> nonce);

internal sealed class AesGcmDocumentCipher
{
    public const byte FormatVersion = 1;
    public const int HeaderSize = 3;
    public const int NonceSize = 12;
    public const int TagSize = 16;
    public const int AssociatedDataSize = HeaderSize + GuidSize;
    public const int Overhead = HeaderSize + NonceSize + TagSize;

    private const int GuidSize = 16;
    private const int CpfLength = 11;
    private const int CnpjLength = 14;
    private const int CpfPayloadSize = CpfLength + Overhead;
    private const int CnpjPayloadSize = CnpjLength + Overhead;

    private readonly NonceFiller _fillNonce;

    public AesGcmDocumentCipher()
        : this(RandomNumberGenerator.Fill)
    {
    }

    internal AesGcmDocumentCipher(NonceFiller fillNonce)
    {
        _fillNonce = fillNonce;
    }

    public static Result<ushort> ReadKeyVersion(ReadOnlySpan<byte> payload)
    {
        if (!HasSupportedShape(payload))
        {
            return DocumentProtectionErrors.Unreadable;
        }

        return BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(1, sizeof(ushort)));
    }

    public byte[] Encrypt(ReadOnlySpan<byte> plaintext, KeySet keys, AccountId accountId)
    {
        if (plaintext.Length is not (CpfLength or CnpjLength))
        {
            throw new ArgumentException("The document must be a normalized CPF or CNPJ.", nameof(plaintext));
        }

        var payload = new byte[Overhead + plaintext.Length];
        var header = payload.AsSpan(0, HeaderSize);
        var nonce = payload.AsSpan(HeaderSize, NonceSize);
        var ciphertext = payload.AsSpan(HeaderSize + NonceSize, plaintext.Length);
        var tag = payload.AsSpan(HeaderSize + NonceSize + plaintext.Length, TagSize);

        header[0] = FormatVersion;
        BinaryPrimitives.WriteUInt16BigEndian(header[1..], keys.Version);
        _fillNonce(nonce);

        Span<byte> associatedData = stackalloc byte[AssociatedDataSize];
        WriteAssociatedData(header, accountId, associatedData);

        using var aes = new AesGcm(keys.EncryptionKey.Span, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

        return payload;
    }

    public Result<byte[]> Decrypt(ReadOnlySpan<byte> payload, AccountId accountId, KeySet keys)
    {
        if (!HasSupportedShape(payload))
        {
            return DocumentProtectionErrors.Unreadable;
        }

        var plaintextLength = payload.Length - Overhead;
        var header = payload[..HeaderSize];

        if (BinaryPrimitives.ReadUInt16BigEndian(header[1..]) != keys.Version)
        {
            return DocumentProtectionErrors.Unreadable;
        }

        var nonce = payload.Slice(HeaderSize, NonceSize);
        var ciphertext = payload.Slice(HeaderSize + NonceSize, plaintextLength);
        var tag = payload.Slice(HeaderSize + NonceSize + plaintextLength, TagSize);

        Span<byte> associatedData = stackalloc byte[AssociatedDataSize];
        WriteAssociatedData(header, accountId, associatedData);

        var plaintext = new byte[plaintextLength];

        try
        {
            using var aes = new AesGcm(keys.EncryptionKey.Span, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);

            return plaintext;
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(plaintext);

            return DocumentProtectionErrors.Unreadable;
        }
    }

    private static bool HasSupportedShape(ReadOnlySpan<byte> payload)
    {
        if (payload.Length is not (CpfPayloadSize or CnpjPayloadSize))
        {
            return false;
        }

        return payload[0] == FormatVersion;
    }

    private static void WriteAssociatedData(ReadOnlySpan<byte> header, AccountId accountId, Span<byte> destination)
    {
        header.CopyTo(destination);

        if (!accountId.Value.TryWriteBytes(destination[HeaderSize..], bigEndian: true, out _))
        {
            throw new InvalidOperationException("The associated data buffer is too small for the account id.");
        }
    }
}
