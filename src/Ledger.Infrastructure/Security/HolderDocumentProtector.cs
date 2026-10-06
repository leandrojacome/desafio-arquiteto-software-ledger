using System.Security.Cryptography;
using System.Text;
using Ledger.Application.Abstractions;
using Ledger.Application.Security;
using Ledger.Domain.Accounts;
using Ledger.Domain.Shared;

namespace Ledger.Infrastructure.Security;

internal sealed class HolderDocumentProtector(IKeyProvider keyProvider, AesGcmDocumentCipher cipher)
    : IHolderDocumentProtector
{
    public ProtectedHolderDocument Protect(HolderDocument document, AccountId accountId)
    {
        return Protect(document.Normalized, accountId);
    }

    public Result<HolderDocument> Unprotect(ReadOnlyMemory<byte> encrypted, AccountId accountId)
    {
        var plaintext = Decrypt(encrypted, accountId);

        if (plaintext.IsFailure)
        {
            return plaintext.Error;
        }

        try
        {
            return HolderDocument.From(Encoding.UTF8.GetString(plaintext.Value));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext.Value);
        }
    }

    public Result<ProtectedHolderDocument> Reprotect(ReadOnlyMemory<byte> encrypted, AccountId accountId)
    {
        var plaintext = Decrypt(encrypted, accountId);

        if (plaintext.IsFailure)
        {
            return plaintext.Error;
        }

        try
        {
            return Seal(plaintext.Value, accountId);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext.Value);
        }
    }

    public IReadOnlyList<ReadOnlyMemory<byte>> BlindIndexCandidates(HolderDocument document)
    {
        EnsureAvailable();

        return [.. keyProvider.Live
            .OrderBy(keys => keys.Version)
            .Select(keys => (ReadOnlyMemory<byte>)HmacBlindIndex.Compute(keys.BlindIndexKey.Span, document.Normalized))];
    }

    private ProtectedHolderDocument Protect(string normalized, AccountId accountId)
    {
        var plaintext = Encoding.UTF8.GetBytes(normalized);

        try
        {
            return Seal(plaintext, accountId);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private ProtectedHolderDocument Seal(ReadOnlySpan<byte> plaintext, AccountId accountId)
    {
        var keys = keyProvider.Active;

        return new ProtectedHolderDocument(
            cipher.Encrypt(plaintext, keys, accountId),
            HmacBlindIndex.Compute(keys.BlindIndexKey.Span, plaintext),
            keys.Version);
    }

    private Result<byte[]> Decrypt(ReadOnlyMemory<byte> encrypted, AccountId accountId)
    {
        EnsureAvailable();

        var version = AesGcmDocumentCipher.ReadKeyVersion(encrypted.Span);

        if (version.IsFailure)
        {
            return version.Error;
        }

        KeySet keys;

        try
        {
            keys = keyProvider.Get(version.Value);
        }
        catch (KeyNotFoundException)
        {
            return DocumentProtectionErrors.Unreadable;
        }

        return cipher.Decrypt(encrypted.Span, accountId, keys);
    }

    private void EnsureAvailable()
    {
        if (!keyProvider.IsAvailable)
        {
            throw new KeyProviderUnavailableException();
        }
    }
}
