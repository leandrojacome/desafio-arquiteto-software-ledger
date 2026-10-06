using System.Security.Cryptography;
using System.Text;
using Ledger.Application.Security;
using Ledger.Domain.Accounts;

namespace Ledger.Infrastructure.Security;

internal static class KeyRoundTrip
{
    private const string ProbeDocument = "52998224725";
    private static readonly Guid ProbeAccount = new("0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33");

    public static bool Succeeds(KeySet keys)
    {
        var accountId = AccountId.From(ProbeAccount).Value;
        var plaintext = Encoding.UTF8.GetBytes(ProbeDocument);
        var cipher = new AesGcmDocumentCipher();

        try
        {
            var payload = cipher.Encrypt(plaintext, keys, accountId);
            var decrypted = cipher.Decrypt(payload, accountId, keys);

            if (decrypted.IsFailure)
            {
                return false;
            }

            var matches = CryptographicOperations.FixedTimeEquals(decrypted.Value, plaintext);
            CryptographicOperations.ZeroMemory(decrypted.Value);

            return matches && HmacBlindIndex.Compute(keys.BlindIndexKey.Span, ProbeDocument).Length == HmacBlindIndex.Size;
        }
        catch (CryptographicException)
        {
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
