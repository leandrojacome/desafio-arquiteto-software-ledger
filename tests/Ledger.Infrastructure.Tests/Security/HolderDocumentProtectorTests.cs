using Ledger.Application.Security;
using Ledger.Application.Tests.Security;
using Ledger.Domain.Accounts;
using Ledger.Infrastructure.Security;

namespace Ledger.Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public sealed class HolderDocumentProtectorTests
{
    private static HolderDocument Document(string raw) => HolderDocument.From(raw).Value;

    private static HolderDocumentProtector ProtectorOver(FakeKeyProvider provider) =>
        new(provider, new AesGcmDocumentCipher());

    private static FakeKeyProvider ActiveOne() => new(1, SecurityVectors.KeySetOne);

    private static FakeKeyProvider ActiveTwo() => new(2, SecurityVectors.KeySetOne, SecurityVectors.KeySetTwo);

    [Fact]
    public void Protect_Cpf_ProducesAFortyTwoByteBlobWithTheActiveVersion()
    {
        var protectedDocument = ProtectorOver(ActiveOne()).Protect(Document("123.456.789-09"), SecurityVectors.Account);

        protectedDocument.Encrypted.Length.ShouldBe(42);
        protectedDocument.KeyVersion.ShouldBe(1);
        protectedDocument.BlindIndex.Length.ShouldBe(32);
        AesGcmDocumentCipher.ReadKeyVersion(protectedDocument.Encrypted.Span).Value.ShouldBe((ushort)1);
    }

    [Fact]
    public void Protect_Cnpj_ProducesAFortyFiveByteBlob()
    {
        var protectedDocument = ProtectorOver(ActiveOne()).Protect(Document("12.ABC.345/01DE-35"), SecurityVectors.Account);

        protectedDocument.Encrypted.Length.ShouldBe(45);
    }

    [Fact]
    public void Protect_BlindIndex_IsTheHmacOfTheNormalizedDocumentWithTheActiveIndexKey()
    {
        var protectedDocument = ProtectorOver(ActiveTwo()).Protect(Document("123.456.789-09"), SecurityVectors.Account);

        protectedDocument.KeyVersion.ShouldBe(2);
        protectedDocument.BlindIndex.ToArray().ShouldBe(
            HmacBlindIndex.Compute(SecurityVectors.KeySetTwo.BlindIndexKey.Span, SecurityVectors.CpfDocument));
    }

    [Fact]
    public void Protect_SameDocumentTwice_GivesTheSameIndexAndDifferentBlobs()
    {
        var protector = ProtectorOver(ActiveOne());

        var first = protector.Protect(Document("123.456.789-09"), SecurityVectors.Account);
        var second = protector.Protect(Document("12345678909"), SecurityVectors.Account);

        first.BlindIndex.ToArray().ShouldBe(second.BlindIndex.ToArray());
        first.Encrypted.ToArray().ShouldNotBe(second.Encrypted.ToArray());
    }

    [Fact]
    public void Protect_DifferentDocuments_GiveDifferentIndexes()
    {
        var protector = ProtectorOver(ActiveOne());

        var first = protector.Protect(Document("123.456.789-09"), SecurityVectors.Account);
        var second = protector.Protect(Document("529.982.247-25"), SecurityVectors.Account);

        first.BlindIndex.ToArray().ShouldNotBe(second.BlindIndex.ToArray());
    }

    [Fact]
    public void Unprotect_BlobProtectedByTheSameProvider_ReturnsTheDocument()
    {
        var protector = ProtectorOver(ActiveOne());
        var protectedDocument = protector.Protect(Document("12abc34501de35"), SecurityVectors.Account);

        var restored = protector.Unprotect(protectedDocument.Encrypted, SecurityVectors.Account);

        restored.IsSuccess.ShouldBeTrue();
        restored.Value.Normalized.ShouldBe("12ABC34501DE35");
    }

    [Fact]
    public void Unprotect_BlobOfAnOlderVersion_UsesTheVersionInTheHeader()
    {
        var oldBlob = ProtectorOver(ActiveOne()).Protect(Document("123.456.789-09"), SecurityVectors.Account);

        var restored = ProtectorOver(ActiveTwo()).Unprotect(oldBlob.Encrypted, SecurityVectors.Account);

        restored.IsSuccess.ShouldBeTrue();
        restored.Value.Normalized.ShouldBe(SecurityVectors.CpfDocument);
    }

    [Fact]
    public void Unprotect_BlobOfAVersionThatIsNoLongerLive_FailsWithoutThrowing()
    {
        var oldBlob = ProtectorOver(ActiveOne()).Protect(Document("123.456.789-09"), SecurityVectors.Account);
        var onlyTwo = new FakeKeyProvider(2, SecurityVectors.KeySetTwo);

        var restored = ProtectorOver(onlyTwo).Unprotect(oldBlob.Encrypted, SecurityVectors.Account);

        restored.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Unprotect_BlobOfAnotherAccount_Fails()
    {
        var protector = ProtectorOver(ActiveOne());
        var blob = protector.Protect(Document("123.456.789-09"), SecurityVectors.Account);

        protector.Unprotect(blob.Encrypted, SecurityVectors.OtherAccount).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Unprotect_TamperedBlob_Fails()
    {
        var protector = ProtectorOver(ActiveOne());
        var blob = protector.Protect(Document("123.456.789-09"), SecurityVectors.Account).Encrypted.ToArray();
        blob[20] ^= 0x01;

        protector.Unprotect(blob, SecurityVectors.Account).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Reprotect_BlobOfVersionOne_ReturnsABlobAndAnIndexOfTheActiveVersion()
    {
        var oldBlob = ProtectorOver(ActiveOne()).Protect(Document("123.456.789-09"), SecurityVectors.Account);
        var protector = ProtectorOver(ActiveTwo());

        var result = protector.Reprotect(oldBlob.Encrypted, SecurityVectors.Account);

        result.IsSuccess.ShouldBeTrue();
        result.Value.KeyVersion.ShouldBe(2);
        AesGcmDocumentCipher.ReadKeyVersion(result.Value.Encrypted.Span).Value.ShouldBe((ushort)2);
        result.Value.BlindIndex.ToArray().ShouldBe(
            HmacBlindIndex.Compute(SecurityVectors.KeySetTwo.BlindIndexKey.Span, SecurityVectors.CpfDocument));
        result.Value.BlindIndex.ToArray().ShouldNotBe(oldBlob.BlindIndex.ToArray());
        protector.Unprotect(result.Value.Encrypted, SecurityVectors.Account).Value.Normalized
            .ShouldBe(SecurityVectors.CpfDocument);
    }

    [Fact]
    public void Reprotect_KeepsTheAccountBoundToTheNewBlob()
    {
        var oldBlob = ProtectorOver(ActiveOne()).Protect(Document("123.456.789-09"), SecurityVectors.Account);
        var protector = ProtectorOver(ActiveTwo());

        var result = protector.Reprotect(oldBlob.Encrypted, SecurityVectors.Account);

        protector.Unprotect(result.Value.Encrypted, SecurityVectors.OtherAccount).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Reprotect_TamperedBlob_FailsInsteadOfThrowing()
    {
        var protector = ProtectorOver(ActiveTwo());
        var blob = ProtectorOver(ActiveOne()).Protect(Document("123.456.789-09"), SecurityVectors.Account)
            .Encrypted.ToArray();
        blob[^1] ^= 0xFF;

        protector.Reprotect(blob, SecurityVectors.Account).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Reprotect_BlobOfAVersionWhoseKeysWereLost_Fails()
    {
        var oldBlob = ProtectorOver(ActiveOne()).Protect(Document("123.456.789-09"), SecurityVectors.Account);
        var onlyTwo = new FakeKeyProvider(2, SecurityVectors.KeySetTwo);

        ProtectorOver(onlyTwo).Reprotect(oldBlob.Encrypted, SecurityVectors.Account).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void BlindIndexCandidates_ReturnsOneIndexPerLiveVersionInVersionOrder()
    {
        var candidates = ProtectorOver(ActiveTwo()).BlindIndexCandidates(Document("123.456.789-09"));

        candidates.Count.ShouldBe(2);
        candidates[0].ToArray().ShouldBe(
            HmacBlindIndex.Compute(SecurityVectors.KeySetOne.BlindIndexKey.Span, SecurityVectors.CpfDocument));
        candidates[1].ToArray().ShouldBe(
            HmacBlindIndex.Compute(SecurityVectors.KeySetTwo.BlindIndexKey.Span, SecurityVectors.CpfDocument));
    }

    [Fact]
    public void BlindIndexCandidates_ContainTheIndexStoredByAnyLiveVersion()
    {
        var oldRow = ProtectorOver(ActiveOne()).Protect(Document("123.456.789-09"), SecurityVectors.Account);
        var newRow = ProtectorOver(ActiveTwo()).Protect(Document("123.456.789-09"), SecurityVectors.OtherAccount);

        var candidates = ProtectorOver(ActiveTwo())
            .BlindIndexCandidates(Document("123.456.789-09"))
            .Select(candidate => Convert.ToHexString(candidate.Span))
            .ToList();

        candidates.ShouldContain(Convert.ToHexString(oldRow.BlindIndex.Span));
        candidates.ShouldContain(Convert.ToHexString(newRow.BlindIndex.Span));
    }

    [Fact]
    public void Protect_ProviderUnavailable_ThrowsTheTransientKeyException()
    {
        var protector = ProtectorOver(ActiveOne().WentDown());

        Should.Throw<KeyProviderUnavailableException>(() =>
            protector.Protect(Document("123.456.789-09"), SecurityVectors.Account));
    }

    [Fact]
    public void Unprotect_ProviderUnavailable_ThrowsInsteadOfReportingAnUnreadableDocument()
    {
        var blob = ProtectorOver(ActiveOne()).Protect(Document("123.456.789-09"), SecurityVectors.Account);
        var protector = ProtectorOver(ActiveOne().WentDown());

        Should.Throw<KeyProviderUnavailableException>(() =>
            protector.Unprotect(blob.Encrypted, SecurityVectors.Account));
        Should.Throw<KeyProviderUnavailableException>(() =>
            protector.Reprotect(blob.Encrypted, SecurityVectors.Account));
        Should.Throw<KeyProviderUnavailableException>(() =>
            protector.BlindIndexCandidates(Document("123.456.789-09")));
    }

    [Fact]
    public void ProtectedDocument_ToString_PrintsOnlyTheKeyVersion()
    {
        var protectedDocument = ProtectorOver(ActiveOne()).Protect(Document("123.456.789-09"), SecurityVectors.Account);

        var text = protectedDocument.ToString();

        text.ShouldContain("KeyVersion = 1", Case.Sensitive);
        text.ShouldNotContain(Convert.ToBase64String(protectedDocument.Encrypted.Span), Case.Sensitive);
        text.ShouldNotContain(Convert.ToHexString(protectedDocument.BlindIndex.Span), Case.Insensitive);
        text.ShouldNotContain("123", Case.Sensitive);
    }
}
