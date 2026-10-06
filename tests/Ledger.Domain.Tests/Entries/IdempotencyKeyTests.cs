using Ledger.Domain.Entries;

namespace Ledger.Domain.Tests.Entries;

[Trait("Category", "Unit")]
public sealed class IdempotencyKeyTests
{
    private const int MaxLength = 128;

    [Theory]
    [InlineData("a")]
    [InlineData("!")]
    [InlineData("~")]
    [InlineData("!~")]
    [InlineData("0192b7c4-5d11-7a3e-9c2f-3b1e7d9a4f10")]
    [InlineData("E18236120202610011403s0a1b2c3d4e")]
    [InlineData("rev-4f1a9c2e-7b3d-4e58-9a06-c2d81f5b7e30")]
    [InlineData("qs-debit-0001")]
    public void From_WithVisibleAscii_ReturnsTheKeyWithTheSameText(string text)
    {
        var result = IdempotencyKey.From(text);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(text);
    }

    [Fact]
    public void From_WithExactlyTheMaximumLength_IsAccepted()
    {
        var text = new string('k', MaxLength);

        var result = IdempotencyKey.From(text);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(text);
    }

    [Fact]
    public void From_WithOneCharacterAboveTheMaximumLength_ReturnsMalformed()
    {
        var result = IdempotencyKey.From(new string('k', MaxLength + 1));

        result.Error.ShouldBe(EntryErrors.IdempotencyKeyMalformed);
    }

    [Fact]
    public void From_WithAVeryLongText_ReturnsMalformed()
    {
        var result = IdempotencyKey.From(new string('k', 100_000));

        result.Error.ShouldBe(EntryErrors.IdempotencyKeyMalformed);
    }

    [Fact]
    public void MaxLength_IsTheContractLimit()
    {
        IdempotencyKey.MaxLength.ShouldBe(MaxLength);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void From_WithNullOrEmptyText_ReturnsRequired(string? text)
    {
        var result = IdempotencyKey.From(text);

        result.Error.ShouldBe(EntryErrors.IdempotencyKeyRequired);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("a b")]
    [InlineData(" a")]
    [InlineData("a ")]
    [InlineData("\t")]
    [InlineData("a\tb")]
    [InlineData("a\nb")]
    [InlineData("a\r\nb")]
    [InlineData("a\u0000b")]
    [InlineData("a\u007fb")]
    [InlineData("ç")]
    [InlineData("chave-ação")]
    [InlineData("a b")]
    [InlineData("ＡＢ")]
    [InlineData("key-\U0001F600")]
    [InlineData("​key")]
    public void From_WithAnyCharacterOutsideVisibleAscii_ReturnsMalformed(string text)
    {
        var result = IdempotencyKey.From(text);

        result.Error.ShouldBe(EntryErrors.IdempotencyKeyMalformed);
    }

    [Fact]
    public void From_ForEachCharacterUpToLatinExtended_AcceptsExactlyThe0x21To0x7ERange()
    {
        var accepted = new List<int>();

        for (var code = 0; code < 0x250; code++)
        {
            var text = "k" + (char)code + "k";

            if (IdempotencyKey.From(text).IsSuccess)
            {
                accepted.Add(code);
            }
        }

        accepted.ShouldBe(Enumerable.Range(0x21, 0x7E - 0x21 + 1).ToList());
    }

    [Fact]
    public void Value_OfTheDefaultInstance_Throws()
    {
        var unset = default(IdempotencyKey);

        Should.Throw<InvalidOperationException>(() => unset.Value);
    }

    [Fact]
    public void Fingerprint_OfTheDefaultInstance_Throws()
    {
        var unset = default(IdempotencyKey);

        Should.Throw<InvalidOperationException>(() => unset.Fingerprint);
    }

    [Fact]
    public void ToString_OfTheDefaultInstance_DoesNotThrowAndIsEmpty()
    {
        default(IdempotencyKey).ToString().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("qs-debit-0001", "889585dc")]
    [InlineData("E18236120202610011403s0a1b2c3d4e", "e42b8ab0")]
    [InlineData("a", "ca978112")]
    public void Fingerprint_IsTheFirstEightLowercaseHexCharactersOfTheSha256(string text, string expected)
    {
        var key = IdempotencyKey.From(text).Value;

        key.Fingerprint.ShouldBe(expected);
    }

    [Fact]
    public void Fingerprint_AlwaysHasEightLowercaseHexCharacters()
    {
        for (var index = 0; index < 200; index++)
        {
            var key = IdempotencyKey.From("key-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Value;

            key.Fingerprint.Length.ShouldBe(8);
            key.Fingerprint.ShouldAllBe(character => char.IsAsciiHexDigitLower(character));
        }
    }

    [Fact]
    public void Fingerprint_ForKeysThatDifferOnlyInCase_IsDifferent()
    {
        var lower = IdempotencyKey.From("abc").Value;
        var upper = IdempotencyKey.From("ABC").Value;

        lower.Fingerprint.ShouldNotBe(upper.Fingerprint);
    }

    [Theory]
    [InlineData("qs-debit-0001")]
    [InlineData("E18236120202610011403s0a1b2c3d4e")]
    [InlineData("a")]
    public void ToString_ReturnsTheFingerprintAndNeverTheKey(string text)
    {
        var key = IdempotencyKey.From(text).Value;

        var printed = key.ToString();

        printed.ShouldBe(key.Fingerprint);
        printed.ShouldNotBe(text);
    }

    [Fact]
    public void Interpolation_NeverExposesTheKey()
    {
        var key = IdempotencyKey.From("super-secret-client-key-0001").Value;

        var message = $"reserving {key}";

        message.ShouldNotContain("super-secret-client-key-0001");
        message.ShouldContain(key.Fingerprint);
    }

    [Fact]
    public void Equals_ForTheSameText_IsTrue()
    {
        var first = IdempotencyKey.From("same-key").Value;
        var second = IdempotencyKey.From("same-key").Value;

        first.ShouldBe(second);
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }

    [Fact]
    public void Equals_ForTextsThatDifferInCase_IsFalse()
    {
        var lower = IdempotencyKey.From("key").Value;
        var upper = IdempotencyKey.From("KEY").Value;

        lower.ShouldNotBe(upper);
    }
}
