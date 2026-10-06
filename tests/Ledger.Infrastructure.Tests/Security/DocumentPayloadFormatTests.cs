using System.Security.Cryptography;
using Ledger.Application.Security;
using Ledger.Application.Tests.Security;
using Ledger.Infrastructure.Security;

namespace Ledger.Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public sealed class DocumentPayloadFormatTests
{
    private static AesGcmDocumentCipher FixedNonceCipher() => new(nonce => SecurityVectors.Nonce().CopyTo(nonce));

    private static byte[] PublishedCpfBlob() => Convert.FromHexString(SecurityVectors.CpfBlobHex);

    private static byte[] PublishedCnpjBlob() => Convert.FromHexString(SecurityVectors.CnpjBlobHex);

    [Fact]
    public void Encrypt_CpfWithTheVectorNonce_ReproducesThePublishedBlob()
    {
        var blob = FixedNonceCipher().Encrypt(
            System.Text.Encoding.UTF8.GetBytes(SecurityVectors.CpfDocument),
            SecurityVectors.KeySetOne,
            SecurityVectors.Account);

        Convert.ToHexStringLower(blob).ShouldBe(SecurityVectors.CpfBlobHex);
        Convert.ToBase64String(blob).ShouldBe(SecurityVectors.CpfBlobBase64);
    }

    [Fact]
    public void Encrypt_AlphanumericCnpjWithTheVectorNonce_ReproducesThePublishedBlob()
    {
        var blob = FixedNonceCipher().Encrypt(
            System.Text.Encoding.UTF8.GetBytes(SecurityVectors.CnpjAlphanumericDocument),
            SecurityVectors.KeySetOne,
            SecurityVectors.Account);

        Convert.ToHexStringLower(blob).ShouldBe(SecurityVectors.CnpjBlobHex);
        Convert.ToBase64String(blob).ShouldBe(SecurityVectors.CnpjBlobBase64);
    }

    [Fact]
    public void Decrypt_ThePublishedCpfBlob_ReturnsTheDocument()
    {
        var result = new AesGcmDocumentCipher().Decrypt(
            PublishedCpfBlob(),
            SecurityVectors.Account,
            SecurityVectors.KeySetOne);

        result.IsSuccess.ShouldBeTrue();
        System.Text.Encoding.UTF8.GetString(result.Value).ShouldBe(SecurityVectors.CpfDocument);
    }

    [Fact]
    public void Decrypt_ThePublishedCnpjBlob_ReturnsTheDocument()
    {
        var result = new AesGcmDocumentCipher().Decrypt(
            PublishedCnpjBlob(),
            SecurityVectors.Account,
            SecurityVectors.KeySetOne);

        result.IsSuccess.ShouldBeTrue();
        System.Text.Encoding.UTF8.GetString(result.Value).ShouldBe(SecurityVectors.CnpjAlphanumericDocument);
    }

    [Fact]
    public void Blob_HasTheLayoutOfTheContract()
    {
        var blob = PublishedCpfBlob();

        blob[0].ShouldBe((byte)1);
        blob[1].ShouldBe((byte)0);
        blob[2].ShouldBe((byte)1);
        Convert.ToHexStringLower(blob.AsSpan(3, 12)).ShouldBe(SecurityVectors.NonceHex);
        blob.Length.ShouldBe(3 + 12 + 11 + 16);
        PublishedCnpjBlob().Length.ShouldBe(3 + 12 + 14 + 16);
    }

    [Fact]
    public void Decrypt_AccountWithTheLastByteChanged_Fails()
    {
        var result = new AesGcmDocumentCipher().Decrypt(
            PublishedCpfBlob(),
            SecurityVectors.OtherAccount,
            SecurityVectors.KeySetOne);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Decrypt_HeaderChangedToKeyVersionTwo_FailsEvenWithTheSameKeyMaterial()
    {
        var blob = PublishedCpfBlob();
        blob[2] = 2;
        var sameKeysUnderVersionTwo = SecurityVectors.Keys(
            2,
            SecurityVectors.EncryptionKeyBase64,
            SecurityVectors.BlindIndexKeyBase64);

        var result = new AesGcmDocumentCipher().Decrypt(blob, SecurityVectors.Account, sameKeysUnderVersionTwo);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Decrypt_HeaderVersionDifferentFromTheKeySetVersion_Fails()
    {
        var blob = PublishedCpfBlob();
        blob[2] = 2;

        var result = new AesGcmDocumentCipher().Decrypt(blob, SecurityVectors.Account, SecurityVectors.KeySetOne);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Decrypt_CipherTextByteChanged_Fails()
    {
        var blob = PublishedCpfBlob();
        blob[20] ^= 0x01;

        var result = new AesGcmDocumentCipher().Decrypt(blob, SecurityVectors.Account, SecurityVectors.KeySetOne);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Decrypt_TagTruncatedTo15Bytes_IsRefusedBeforeDecrypting()
    {
        var truncated = PublishedCpfBlob().AsSpan(0, 41).ToArray();

        var shape = AesGcmDocumentCipher.ReadKeyVersion(truncated);
        var result = new AesGcmDocumentCipher().Decrypt(truncated, SecurityVectors.Account, SecurityVectors.KeySetOne);

        shape.IsFailure.ShouldBeTrue();
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Decrypt_AnyByteChangedOneAtATime_Fails()
    {
        var original = PublishedCpfBlob();
        var cipher = new AesGcmDocumentCipher();

        for (var index = 0; index < original.Length; index++)
        {
            var mutated = (byte[])original.Clone();
            mutated[index] ^= 0x80;

            cipher.Decrypt(mutated, SecurityVectors.Account, SecurityVectors.KeySetOne)
                .IsFailure.ShouldBeTrue($"changing byte {index} must be detected");
        }
    }

    [Fact]
    public void Decrypt_AnyBitChangedInTheCnpjBlob_Fails()
    {
        var original = PublishedCnpjBlob();
        var cipher = new AesGcmDocumentCipher();

        for (var index = 0; index < original.Length; index++)
        {
            var mutated = (byte[])original.Clone();
            mutated[index] ^= 0x01;

            cipher.Decrypt(mutated, SecurityVectors.Account, SecurityVectors.KeySetOne)
                .IsFailure.ShouldBeTrue($"changing byte {index} must be detected");
        }
    }

    [Fact]
    public void ReadKeyVersion_PublishedBlob_ReturnsTheBigEndianVersion()
    {
        var blob = PublishedCpfBlob();
        blob[1] = 0x01;
        blob[2] = 0x02;

        AesGcmDocumentCipher.ReadKeyVersion(blob).Value.ShouldBe((ushort)258);
    }

    [Fact]
    public void ReadKeyVersion_UnknownFormatByte_Fails()
    {
        var blob = PublishedCpfBlob();
        blob[0] = 2;

        AesGcmDocumentCipher.ReadKeyVersion(blob).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Decrypt_AssociatedDataIsTheHeaderFollowedByTheAccountInTextualOrder()
    {
        var blob = PublishedCpfBlob();
        var key = Convert.FromBase64String(SecurityVectors.EncryptionKeyBase64);
        var plaintext = new byte[11];

        using var aes = new AesGcm(key, 16);
        aes.Decrypt(
            blob.AsSpan(3, 12),
            blob.AsSpan(15, 11),
            blob.AsSpan(26, 16),
            plaintext,
            Convert.FromHexString(SecurityVectors.AssociatedDataHex));

        System.Text.Encoding.UTF8.GetString(plaintext).ShouldBe(SecurityVectors.CpfDocument);
    }

    [Fact]
    public void Encrypt_PlaintextThatIsNotADocumentLength_Throws()
    {
        var cipher = new AesGcmDocumentCipher();
        var keys = SecurityVectors.KeySetOne;

        Should.Throw<ArgumentException>(() =>
            cipher.Encrypt(new byte[10], keys, SecurityVectors.Account));
    }

    [Fact]
    public void Constructor_PublicOneUsesTheSystemRandomSource()
    {
        var cipher = new AesGcmDocumentCipher();
        var first = cipher.Encrypt(
            System.Text.Encoding.UTF8.GetBytes(SecurityVectors.CpfDocument),
            SecurityVectors.KeySetOne,
            SecurityVectors.Account);
        var second = cipher.Encrypt(
            System.Text.Encoding.UTF8.GetBytes(SecurityVectors.CpfDocument),
            SecurityVectors.KeySetOne,
            SecurityVectors.Account);

        first.AsSpan(3, 12).SequenceEqual(second.AsSpan(3, 12)).ShouldBeFalse();
        first.ShouldNotBe(second);
    }
}
