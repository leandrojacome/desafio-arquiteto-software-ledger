using System.Diagnostics.CodeAnalysis;
using Ledger.Domain.Accounts;

namespace Ledger.Domain.Tests.Accounts;

[Trait("Category", "Unit")]
public sealed class HolderDocumentTests
{
    private const int Seed = 20261001;
    private const int GeneratedDocuments = 2000;
    private const string AlphanumericAlphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    [Theory]
    [InlineData("123.456.789-09", "12345678909", HolderDocumentKind.Cpf)]
    [InlineData("529.982.247-25", "52998224725", HolderDocumentKind.Cpf)]
    [InlineData("987.654.321-00", "98765432100", HolderDocumentKind.Cpf)]
    [InlineData("12345678909", "12345678909", HolderDocumentKind.Cpf)]
    [InlineData("100.000.028-10", "10000002810", HolderDocumentKind.Cpf)]
    [InlineData("100.000.036-20", "10000003620", HolderDocumentKind.Cpf)]
    [InlineData("12.345.678/0001-95", "12345678000195", HolderDocumentKind.Cnpj)]
    [InlineData("11.222.333/0001-81", "11222333000181", HolderDocumentKind.Cnpj)]
    [InlineData("12.ABC.345/01DE-35", "12ABC34501DE35", HolderDocumentKind.Cnpj)]
    [InlineData("12abc34501de35", "12ABC34501DE35", HolderDocumentKind.Cnpj)]
    [InlineData("12.abc.345/01de-35", "12ABC34501DE35", HolderDocumentKind.Cnpj)]
    [InlineData("AB1CD2EF3GH490", "AB1CD2EF3GH490", HolderDocumentKind.Cnpj)]
    [InlineData("12SBC34501DE48", "12SBC34501DE48", HolderDocumentKind.Cnpj)]
    [InlineData("12IBC34501DE10", "12IBC34501DE10", HolderDocumentKind.Cnpj)]
    [InlineData("10000001000190", "10000001000190", HolderDocumentKind.Cnpj)]
    [InlineData("10000008000101", "10000008000101", HolderDocumentKind.Cnpj)]
    [InlineData("10000017000100", "10000017000100", HolderDocumentKind.Cnpj)]
    public void From_WithAValidDocument_ReturnsTheNormalizedValueAndTheKind(
        string raw,
        string normalized,
        HolderDocumentKind kind)
    {
        var result = HolderDocument.From(raw);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Normalized.ShouldBe(normalized);
        result.Value.Kind.ShouldBe(kind);
    }

    [Theory]
    [InlineData("123456789.09")]
    [InlineData("123.456.789-09")]
    [InlineData("123-456.789.09")]
    [InlineData("12/3-456789.09")]
    [InlineData("---12345678909---")]
    [InlineData("./-12345678909./-")]
    [InlineData("1.2.3.4.5.6.7.8909")]
    public void From_RemovesDotsHyphensAndSlashesInAnyPosition(string raw)
    {
        var result = HolderDocument.From(raw);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Normalized.ShouldBe("12345678909");
    }

    [Fact]
    public void From_WithDifferentFormattingOfTheSameDocument_ReturnsEqualValues()
    {
        var formatted = HolderDocument.From("123.456.789-09").Value;
        var plain = HolderDocument.From("12345678909").Value;
        var odd = HolderDocument.From("123456789.09").Value;

        formatted.ShouldBe(plain);
        odd.ShouldBe(plain);
        formatted.GetHashCode().ShouldBe(plain.GetHashCode());
    }

    [Fact]
    public void From_RaisesLettersToUppercaseBeforeTheCheckDigits()
    {
        HolderDocument.From("12abc34501de35").IsSuccess.ShouldBeTrue();
        HolderDocument.From("12abc34501de05").Error.ShouldBe(AccountErrors.InvalidHolderDocument);
    }

    [Fact]
    public void From_WithExactlyEighteenRawCharacters_IsAccepted()
    {
        var result = HolderDocument.From("12.345.678/0001-95");

        result.IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("12.345.678/0001-95.")]
    [InlineData("123.456.789-09.....")]
    [InlineData("-----12345678909-----")]
    [InlineData("12.345.678/0001-95 trailing text")]
    public void From_WithMoreThanEighteenRawCharacters_IsRefusedEvenWhenTheCleanedTextWouldBeValid(string raw)
    {
        var result = HolderDocument.From(raw);

        result.Error.ShouldBe(AccountErrors.InvalidHolderDocument);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("...")]
    [InlineData("-")]
    public void From_WithNullEmptyOrOnlySeparators_IsRefused(string? raw)
    {
        var result = HolderDocument.From(raw);

        result.Error.ShouldBe(AccountErrors.InvalidHolderDocument);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("123456789")]
    [InlineData("1234567890")]
    [InlineData("123456789012")]
    [InlineData("1234567890123")]
    [InlineData("123456789012345")]
    [InlineData("12345678901234567")]
    public void From_WithALengthOtherThanElevenOrFourteen_IsRefused(string raw)
    {
        var result = HolderDocument.From(raw);

        result.Error.ShouldBe(AccountErrors.InvalidHolderDocument);
    }

    [Theory]
    [InlineData("1234567890A")]
    [InlineData("A2345678909")]
    [InlineData("12345A78909")]
    [InlineData("1234567890a")]
    [InlineData("ABCDEFGHIJK")]
    public void From_WithALetterInTheCpf_IsRefused(string raw)
    {
        var result = HolderDocument.From(raw);

        result.Error.ShouldBe(AccountErrors.InvalidHolderDocument);
    }

    [Theory]
    [InlineData("12.ABC.345/01DE-A5")]
    [InlineData("12.ABC.345/01DE-3A")]
    [InlineData("12ABC34501DEAB")]
    [InlineData("12abc34501dea5")]
    public void From_WithALetterInTheTwoCheckPositionsOfTheCnpj_IsRefused(string raw)
    {
        var result = HolderDocument.From(raw);

        result.Error.ShouldBe(AccountErrors.InvalidHolderDocument);
    }

    [Theory]
    [InlineData("123 456 789 09")]
    [InlineData("12345678909 ")]
    [InlineData(" 12345678909")]
    [InlineData("12345678909\n")]
    [InlineData("12345678909\t")]
    [InlineData("123,456,789-09")]
    [InlineData("123_456_789_09")]
    [InlineData("+12345678909")]
    [InlineData("12345678909\u0000")]
    [InlineData("123*456*789*09")]
    [InlineData("12.ABC.345/01DE-3 5")]
    [InlineData("12.ABC.345\\01DE-35")]
    [InlineData("12.ABC.345/01DE:35")]
    public void From_WithSpaceOrAnyOtherSymbol_IsRefused(string raw)
    {
        var result = HolderDocument.From(raw);

        result.Error.ShouldBe(AccountErrors.InvalidHolderDocument);
    }

    [Theory]
    [InlineData("١٢٣٤٥٦٧٨٩٠٩")]
    [InlineData("１２３４５６７８９０９")]
    [InlineData("1234567890¹")]
    public void From_WithDigitsThatAreNotAsciiDigits_IsRefused(string raw)
    {
        var result = HolderDocument.From(raw);

        result.Error.ShouldBe(AccountErrors.InvalidHolderDocument);
    }

    [Theory]
    [InlineData("12ſBC34501DE48")]
    [InlineData("12ıBC34501DE10")]
    [InlineData("12KBC34501DE63")]
    public void From_WithNonAsciiLettersThatLookLikeAsciiOnes_IsRefused(string raw)
    {
        var result = HolderDocument.From(raw);

        result.Error.ShouldBe(AccountErrors.InvalidHolderDocument);
    }

    [Theory]
    [InlineData("123.456.789-00")]
    [InlineData("123.456.789-19")]
    [InlineData("123.456.789-08")]
    [InlineData("12.ABC.345/01DE-34")]
    [InlineData("12.ABC.345/01DE-25")]
    [InlineData("12.345.678/0001-96")]
    [InlineData("12.345.678/0001-05")]
    [InlineData("11.222.333/0001-80")]
    public void From_WithWrongCheckDigits_IsRefused(string raw)
    {
        var result = HolderDocument.From(raw);

        result.Error.ShouldBe(AccountErrors.InvalidHolderDocument);
    }

    [Theory]
    [InlineData("00000000000")]
    [InlineData("11111111111")]
    [InlineData("22222222222")]
    [InlineData("33333333333")]
    [InlineData("44444444444")]
    [InlineData("55555555555")]
    [InlineData("66666666666")]
    [InlineData("77777777777")]
    [InlineData("88888888888")]
    [InlineData("99999999999")]
    [InlineData("111.111.111-11")]
    [InlineData("00000000000000")]
    [InlineData("00.000.000/0000-00")]
    [InlineData("11111111111111")]
    [InlineData("99999999999999")]
    public void From_WithASingleRepeatedCharacter_IsRefusedEvenWhenTheCheckDigitsMatch(string raw)
    {
        var result = HolderDocument.From(raw);

        result.Error.ShouldBe(AccountErrors.InvalidHolderDocument);
    }

    [Fact]
    public void From_WithAllRepeatedDocumentsOfBothKinds_RefusesEveryOne()
    {
        foreach (var character in AlphanumericAlphabet)
        {
            HolderDocument.From(new string(character, 11)).IsFailure.ShouldBeTrue();
            HolderDocument.From(new string(character, 14)).IsFailure.ShouldBeTrue();
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394",
        Justification = "A fixed seed makes the generated documents reproducible; no security decision depends on it.")]
    public void From_WithTwoThousandGeneratedValidDocuments_AcceptsEveryOneWithTheRightKind()
    {
        var random = new Random(Seed);

        for (var index = 0; index < GeneratedDocuments; index++)
        {
            var (document, kind) = index switch
            {
                _ when index % 3 == 0 => (HolderDocumentGenerator.Cpf(random), HolderDocumentKind.Cpf),
                _ when index % 3 == 1 => (HolderDocumentGenerator.Cnpj(random, alphanumeric: false), HolderDocumentKind.Cnpj),
                _ => (HolderDocumentGenerator.Cnpj(random, alphanumeric: true), HolderDocumentKind.Cnpj)
            };

            var result = HolderDocument.From(document);

            result.IsSuccess.ShouldBeTrue(document);
            result.Value.Normalized.ShouldBe(document);
            result.Value.Kind.ShouldBe(kind);
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394",
        Justification = "A fixed seed makes the generated documents reproducible; no security decision depends on it.")]
    [SuppressMessage("Globalization", "CA1308",
        Justification = "The test needs the lowercase spelling of a document to prove it is normalized to uppercase.")]
    public void From_WithGeneratedDocumentsInTheirFormattedAndLowercaseForms_NormalizesToTheSameValue()
    {
        var random = new Random(Seed + 1);

        for (var index = 0; index < GeneratedDocuments; index++)
        {
            var document = index % 2 == 0 ? HolderDocumentGenerator.Cpf(random) : HolderDocumentGenerator.Cnpj(random, alphanumeric: true);
            var formatted = HolderDocumentGenerator.Format(document);
            var lowercase = document.ToLowerInvariant();

            HolderDocument.From(formatted).Value.Normalized.ShouldBe(document);
            HolderDocument.From(lowercase).Value.Normalized.ShouldBe(document);
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394",
        Justification = "A fixed seed makes the generated documents reproducible; no security decision depends on it.")]
    public void From_WithGeneratedDocumentsWhoseLastDigitIsChanged_RefusesEveryOne()
    {
        var random = new Random(Seed + 2);

        for (var index = 0; index < GeneratedDocuments; index++)
        {
            var document = index % 2 == 0 ? HolderDocumentGenerator.Cpf(random) : HolderDocumentGenerator.Cnpj(random, alphanumeric: true);
            var last = document[^1] - '0';
            var changed = document[..^1] + (char)('0' + ((last + 1 + (index % 9)) % 10));

            HolderDocument.From(changed).IsFailure.ShouldBeTrue(changed);
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394",
        Justification = "A fixed seed makes the generated documents reproducible; no security decision depends on it.")]
    public void From_WithGeneratedDocumentsWhoseFirstCheckDigitIsChanged_RefusesEveryOne()
    {
        var random = new Random(Seed + 3);

        for (var index = 0; index < GeneratedDocuments; index++)
        {
            var document = index % 2 == 0 ? HolderDocumentGenerator.Cpf(random) : HolderDocumentGenerator.Cnpj(random, alphanumeric: true);
            var position = document.Length - 2;
            var digit = document[position] - '0';
            var changed = string.Concat(
                document.AsSpan(0, position),
                ((char)('0' + ((digit + 1) % 10))).ToString(),
                document.AsSpan(position + 1));

            HolderDocument.From(changed).IsFailure.ShouldBeTrue(changed);
        }
    }

    [Fact]
    public void Normalized_OfTheDefaultInstance_Throws()
    {
        var unset = default(HolderDocument);

        Should.Throw<InvalidOperationException>(() => unset.Normalized);
    }

    [Fact]
    public void Kind_OfTheDefaultInstance_Throws()
    {
        var unset = default(HolderDocument);

        Should.Throw<InvalidOperationException>(() => unset.Kind);
    }

    [Fact]
    public void ToString_NeverPrintsTheDocument()
    {
        var document = HolderDocument.From("123.456.789-09").Value;

        var printed = document.ToString();
        var interpolated = $"account holder {document}";

        printed.ShouldNotContain("12345678909");
        printed.ShouldNotContain("123456");
        interpolated.ShouldNotContain("12345678909");
        printed.ShouldBe(document.Masked());
    }

    [Fact]
    public void ToString_OfTheDefaultInstance_DoesNotThrowAndIsEmpty()
    {
        default(HolderDocument).ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Kind_StartsAtOneSoTheDefaultNeverPassesForAValidKind()
    {
        ((int)HolderDocumentKind.Cpf).ShouldBe(1);
        ((int)HolderDocumentKind.Cnpj).ShouldBe(2);
        Enum.IsDefined(default(HolderDocumentKind)).ShouldBeFalse();
    }
}
