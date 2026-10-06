using System.Security.Cryptography;
using System.Text;
using Ledger.Application.Tests.Security;
using Ledger.Infrastructure.Security;

namespace Ledger.Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public sealed class HmacBlindIndexTests
{
    private static byte[] IndexKey => Convert.FromBase64String(SecurityVectors.BlindIndexKeyBase64);

    private static byte[] OtherIndexKey => Convert.FromBase64String(SecurityVectors.SecondBlindIndexKeyBase64);

    [Theory]
    [InlineData(SecurityVectors.CpfDocument, SecurityVectors.CpfIndexHex)]
    [InlineData(SecurityVectors.CnpjNumericDocument, SecurityVectors.CnpjNumericIndexHex)]
    [InlineData(SecurityVectors.CnpjAlphanumericDocument, SecurityVectors.CnpjAlphanumericIndexHex)]
    public void Compute_PublishedVectors_ReproduceTheContractValues(string document, string expectedHex)
    {
        var index = HmacBlindIndex.Compute(IndexKey, document);

        Convert.ToHexStringLower(index).ShouldBe(expectedHex);
        index.Length.ShouldBe(32);
    }

    [Fact]
    public void Compute_SameDocumentAndKey_IsDeterministic()
    {
        HmacBlindIndex.Compute(IndexKey, SecurityVectors.CpfDocument)
            .ShouldBe(HmacBlindIndex.Compute(IndexKey, SecurityVectors.CpfDocument));
    }

    [Fact]
    public void Compute_DifferentKey_GivesADifferentIndex()
    {
        HmacBlindIndex.Compute(IndexKey, SecurityVectors.CpfDocument)
            .ShouldNotBe(HmacBlindIndex.Compute(OtherIndexKey, SecurityVectors.CpfDocument));
    }

    [Fact]
    public void Compute_DifferentDocument_GivesADifferentIndex()
    {
        HmacBlindIndex.Compute(IndexKey, SecurityVectors.CpfDocument)
            .ShouldNotBe(HmacBlindIndex.Compute(IndexKey, SecurityVectors.CnpjNumericDocument));
    }

    [Fact]
    public void Compute_UsesTheDomainSeparationPrefix()
    {
        var expected = HMACSHA256.HashData(IndexKey, Encoding.UTF8.GetBytes("holder-document:" + SecurityVectors.CpfDocument));
        var withoutPrefix = HMACSHA256.HashData(IndexKey, Encoding.UTF8.GetBytes(SecurityVectors.CpfDocument));

        var index = HmacBlindIndex.Compute(IndexKey, SecurityVectors.CpfDocument);

        index.ShouldBe(expected);
        index.ShouldNotBe(withoutPrefix);
    }

    [Fact]
    public void Compute_BytesAndTextOverloads_Agree()
    {
        HmacBlindIndex.Compute(IndexKey, Encoding.UTF8.GetBytes(SecurityVectors.CpfDocument))
            .ShouldBe(HmacBlindIndex.Compute(IndexKey, SecurityVectors.CpfDocument));
    }

    [Fact]
    public void Compute_ForTheSameDocumentAndKey_GivesTheKnownIndex()
    {
        var index = Convert.FromHexString(SecurityVectors.CpfIndexHex);

        HmacBlindIndex.Compute(IndexKey, SecurityVectors.CpfDocument).ShouldBe(index);
    }

    [Fact]
    public void Compute_ForAnotherDocumentOrAnotherKey_GivesAnotherIndex()
    {
        var index = Convert.FromHexString(SecurityVectors.CpfIndexHex);

        HmacBlindIndex.Compute(IndexKey, SecurityVectors.CnpjNumericDocument).ShouldNotBe(index);
        HmacBlindIndex.Compute(OtherIndexKey, SecurityVectors.CpfDocument).ShouldNotBe(index);
    }
}
