using System.Text;
using Ledger.Application.Tests.Security;
using Ledger.Infrastructure.Security;

namespace Ledger.Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public sealed class AesGcmDocumentCipherTests
{
    private static byte[] Plaintext(string document) => Encoding.UTF8.GetBytes(document);

    [Theory]
    [InlineData(SecurityVectors.CpfDocument)]
    [InlineData(SecurityVectors.CnpjNumericDocument)]
    [InlineData(SecurityVectors.CnpjAlphanumericDocument)]
    public void EncryptThenDecrypt_ReturnsTheSameDocument(string document)
    {
        var cipher = new AesGcmDocumentCipher();

        var blob = cipher.Encrypt(Plaintext(document), SecurityVectors.KeySetOne, SecurityVectors.Account);
        var result = cipher.Decrypt(blob, SecurityVectors.Account, SecurityVectors.KeySetOne);

        result.IsSuccess.ShouldBeTrue();
        Encoding.UTF8.GetString(result.Value).ShouldBe(document);
    }

    [Fact]
    public void Encrypt_WritesTheFormatByteAndTheKeySetVersionOfTheKeys()
    {
        var blob = new AesGcmDocumentCipher().Encrypt(
            Plaintext(SecurityVectors.CpfDocument),
            SecurityVectors.KeySetTwo,
            SecurityVectors.Account);

        blob[0].ShouldBe((byte)1);
        blob[1].ShouldBe((byte)0);
        blob[2].ShouldBe((byte)2);
        AesGcmDocumentCipher.ReadKeyVersion(blob).Value.ShouldBe((ushort)2);
    }

    [Fact]
    public void Decrypt_WithTheWrongKey_Fails()
    {
        var cipher = new AesGcmDocumentCipher();
        var wrongKeys = SecurityVectors.Keys(1, SecurityVectors.SecondEncryptionKeyBase64, SecurityVectors.BlindIndexKeyBase64);
        var blob = cipher.Encrypt(Plaintext(SecurityVectors.CpfDocument), SecurityVectors.KeySetOne, SecurityVectors.Account);

        cipher.Decrypt(blob, SecurityVectors.Account, wrongKeys).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Decrypt_BlobMovedToAnotherAccount_Fails()
    {
        var cipher = new AesGcmDocumentCipher();
        var blob = cipher.Encrypt(Plaintext(SecurityVectors.CpfDocument), SecurityVectors.KeySetOne, SecurityVectors.Account);

        cipher.Decrypt(blob, SecurityVectors.OtherAccount, SecurityVectors.KeySetOne).IsFailure.ShouldBeTrue();
    }

    [Theory]
    [InlineData(3)]
    [InlineData(14)]
    [InlineData(20)]
    [InlineData(28)]
    public void Decrypt_NonceCipherTextOrTagByteChanged_Fails(int index)
    {
        var cipher = new AesGcmDocumentCipher();
        var blob = cipher.Encrypt(Plaintext(SecurityVectors.CpfDocument), SecurityVectors.KeySetOne, SecurityVectors.Account);
        blob[index] ^= 0x10;

        cipher.Decrypt(blob, SecurityVectors.Account, SecurityVectors.KeySetOne).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Decrypt_BlobOfAnUnexpectedLength_Fails()
    {
        var cipher = new AesGcmDocumentCipher();
        var blob = cipher.Encrypt(Plaintext(SecurityVectors.CpfDocument), SecurityVectors.KeySetOne, SecurityVectors.Account);

        cipher.Decrypt(blob.AsSpan(0, 30), SecurityVectors.Account, SecurityVectors.KeySetOne).IsFailure.ShouldBeTrue();
        cipher.Decrypt([], SecurityVectors.Account, SecurityVectors.KeySetOne).IsFailure.ShouldBeTrue();
        cipher.Decrypt(new byte[100], SecurityVectors.Account, SecurityVectors.KeySetOne).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Decrypt_Failure_DoesNotCarryTheKeyOrTheDocument()
    {
        var cipher = new AesGcmDocumentCipher();
        var blob = cipher.Encrypt(Plaintext(SecurityVectors.CpfDocument), SecurityVectors.KeySetOne, SecurityVectors.Account);

        var result = cipher.Decrypt(blob, SecurityVectors.OtherAccount, SecurityVectors.KeySetOne);

        result.Error.Message.ShouldNotContain(SecurityVectors.CpfDocument, Case.Insensitive);
        result.Error.Message.ShouldNotContain(SecurityVectors.EncryptionKeyBase64, Case.Insensitive);
    }
}
