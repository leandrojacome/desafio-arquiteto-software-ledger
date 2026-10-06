using Ledger.Domain.Entries;

namespace Ledger.Domain.Tests.Entries;

[Trait("Category", "Unit")]
public sealed class EntryTypeTextTests
{
    [Theory]
    [InlineData("CREDIT", EntryType.Credit)]
    [InlineData("DEBIT", EntryType.Debit)]
    public void TryParse_WithTheExactText_ReturnsTheType(string text, EntryType expected)
    {
        var parsed = EntryTypeText.TryParse(text, out var type);

        parsed.ShouldBeTrue();
        type.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("credit")]
    [InlineData("Debit")]
    [InlineData(" CREDIT")]
    [InlineData("DEBIT ")]
    [InlineData("1")]
    [InlineData("CREDIT,DEBIT")]
    [InlineData("TRANSFER")]
    public void TryParse_WithAnythingElse_ReturnsFalseAndTheDefault(string? text)
    {
        var parsed = EntryTypeText.TryParse(text, out var type);

        parsed.ShouldBeFalse();
        type.ShouldBe(default);
    }

    [Theory]
    [InlineData(EntryType.Credit)]
    [InlineData(EntryType.Debit)]
    public void DatabaseText_RoundTripsEveryDefinedType(EntryType original)
    {
        EntryTypeText.TryParse(original.ToDatabaseText(), out var copy).ShouldBeTrue();

        copy.ShouldBe(original);
    }
}
