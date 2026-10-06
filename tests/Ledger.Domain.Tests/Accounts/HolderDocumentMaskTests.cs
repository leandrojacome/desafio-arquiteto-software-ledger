using System.Diagnostics.CodeAnalysis;
using Ledger.Domain.Accounts;

namespace Ledger.Domain.Tests.Accounts;

[Trait("Category", "Unit")]
public sealed class HolderDocumentMaskTests
{
    private const int Seed = 20261001;

    [Theory]
    [InlineData("12345678909", "***.***.789-**")]
    [InlineData("123.456.789-09", "***.***.789-**")]
    [InlineData("52998224725", "***.***.247-**")]
    [InlineData("98765432100", "***.***.321-**")]
    [InlineData("12345678000195", "**.***.***/0001-**")]
    [InlineData("12.345.678/0001-95", "**.***.***/0001-**")]
    [InlineData("11222333000181", "**.***.***/0001-**")]
    [InlineData("12ABC34501DE35", "**.***.***/01DE-**")]
    [InlineData("12abc34501de35", "**.***.***/01DE-**")]
    [InlineData("AB1CD2EF3GH490", "**.***.***/3GH4-**")]
    public void Masked_ShowsOnlyTheAgreedPositions(string raw, string expected)
    {
        var document = HolderDocument.From(raw).Value;

        document.Masked().ShouldBe(expected);
    }

    [Fact]
    public void Masked_ForACpf_HasFourteenCharactersAndExactlyThreeDigits()
    {
        var mask = HolderDocument.From("12345678909").Value.Masked();

        mask.Length.ShouldBe(14);
        mask.Count(char.IsAsciiDigit).ShouldBe(3);
        mask.Count(character => character == '*').ShouldBe(8);
    }

    [Fact]
    public void Masked_ForACnpj_HasEighteenCharactersAndExactlyFourVisibleCharacters()
    {
        var mask = HolderDocument.From("12345678000195").Value.Masked();

        mask.Length.ShouldBe(18);
        mask.Count(char.IsAsciiLetterOrDigit).ShouldBe(4);
        mask.Count(character => character == '*').ShouldBe(10);
    }

    [Fact]
    public void Masked_DoesNotRevealTheCheckDigitsNorTheLeadingDigits()
    {
        var cpf = HolderDocument.From("52998224725").Value.Masked();
        var cnpj = HolderDocument.From("11222333000181").Value.Masked();

        cpf.ShouldNotContain("529");
        cpf.ShouldNotContain("25");
        cnpj.ShouldNotContain("11222333");
        cnpj.ShouldNotContain("81");
    }

    [Fact]
    [SuppressMessage("Security", "CA5394",
        Justification = "A fixed seed makes the generated documents reproducible; no security decision depends on it.")]
    public void Masked_ForGeneratedDocuments_AlwaysRevealsOnlyTheAgreedSlice()
    {
        var random = new Random(Seed);

        for (var index = 0; index < 500; index++)
        {
            var cpf = HolderDocument.From(HolderDocumentGenerator.Cpf(random)).Value;
            var cnpj = HolderDocument.From(HolderDocumentGenerator.Cnpj(random, alphanumeric: true)).Value;

            cpf.Masked().ShouldBe($"***.***.{cpf.Normalized.Substring(6, 3)}-**");
            cnpj.Masked().ShouldBe($"**.***.***/{cnpj.Normalized.Substring(8, 4)}-**");
        }
    }

    [Fact]
    public void Masked_IsStableAcrossCalls()
    {
        var document = HolderDocument.From("10000002810").Value;

        document.Masked().ShouldBe("***.***.028-**");
        document.Masked().ShouldBe(document.Masked());
    }

    [Fact]
    public void Masked_OfTheDefaultInstance_Throws()
    {
        var unset = default(HolderDocument);

        Should.Throw<InvalidOperationException>(() => unset.Masked());
    }
}
