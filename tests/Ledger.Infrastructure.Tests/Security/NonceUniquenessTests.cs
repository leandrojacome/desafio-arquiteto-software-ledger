using System.Text;
using Ledger.Application.Tests.Security;
using Ledger.Infrastructure.Security;

namespace Ledger.Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public sealed class NonceUniquenessTests
{
    private const int Encryptions = 100_000;

    [Fact]
    public void Encrypt_OneHundredThousandTimesWithTheSameKey_NeverRepeatsANonce()
    {
        var cipher = new AesGcmDocumentCipher();
        var plaintext = Encoding.UTF8.GetBytes(SecurityVectors.CpfDocument);
        var keys = SecurityVectors.KeySetOne;
        var seen = new HashSet<string>(Encryptions, StringComparer.Ordinal);

        for (var count = 0; count < Encryptions; count++)
        {
            var blob = cipher.Encrypt(plaintext, keys, SecurityVectors.Account);

            seen.Add(Convert.ToBase64String(blob.AsSpan(3, 12))).ShouldBeTrue($"nonce repeated at encryption {count}");
        }

        seen.Count.ShouldBe(Encryptions);
    }

    [Fact]
    public void Encrypt_TwoCiphersWithTheSameFixedSourceFedByThePublicConstructor_DoNotShareNonces()
    {
        var plaintext = Encoding.UTF8.GetBytes(SecurityVectors.CnpjAlphanumericDocument);
        var first = new AesGcmDocumentCipher().Encrypt(plaintext, SecurityVectors.KeySetOne, SecurityVectors.Account);
        var second = new AesGcmDocumentCipher().Encrypt(plaintext, SecurityVectors.KeySetOne, SecurityVectors.Account);

        Convert.ToHexString(first.AsSpan(3, 12)).ShouldNotBe(Convert.ToHexString(second.AsSpan(3, 12)));
    }
}
