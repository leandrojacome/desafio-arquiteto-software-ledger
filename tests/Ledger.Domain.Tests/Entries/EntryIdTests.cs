using Ledger.Domain.Entries;

namespace Ledger.Domain.Tests.Entries;

[Trait("Category", "Unit")]
public sealed class EntryIdTests
{
    private const string Canonical = "0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d44";

    [Fact]
    public void From_WithValidGuid_ReturnsEntryId()
    {
        var guid = Guid.NewGuid();

        var result = EntryId.From(guid);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(guid);
    }

    [Fact]
    public void From_WithEmptyGuid_ReturnsNotFound()
    {
        var result = EntryId.From(Guid.Empty);

        result.Error.ShouldBe(EntryErrors.NotFound);
    }

    [Fact]
    public void ToString_FormatsTheGuidWithHyphensAndNoBraces()
    {
        var guid = Guid.NewGuid();

        EntryId.From(guid).Value.ToString().ShouldBe(guid.ToString("D"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("zzzzzzzz-zzzz-zzzz-zzzz-zzzzzzzzzzzz")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void From_WithTextThatIsNotAGuid_ReturnsNotFound(string? text)
    {
        var result = EntryId.From(text);

        result.Error.ShouldBe(EntryErrors.NotFound);
    }

    [Theory]
    [InlineData("0192b7c281aa7e04b1d56f0c2a9e8d44")]
    [InlineData("{0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d44}")]
    [InlineData("(0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d44)")]
    [InlineData(" 0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d44")]
    [InlineData("0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d44 ")]
    [InlineData("+192b7c2-81aa-7e04-b1d5-6f0c2a9e8d44")]
    [InlineData("0x92b7c2-81aa-7e04-b1d5-6f0c2a9e8d44")]
    [InlineData("0192b7c2-0xaa-7e04-b1d5-6f0c2a9e8d44")]
    public void From_WithAnyFormatOtherThanHyphenated_ReturnsNotFound(string text)
    {
        var result = EntryId.From(text);

        result.Error.ShouldBe(EntryErrors.NotFound);
    }

    [Fact]
    public void From_WithHyphenatedText_ParsesIt()
    {
        var result = EntryId.From(Canonical);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ToString().ShouldBe(Canonical);
    }

    [Fact]
    public void From_WithUppercaseHyphenatedText_ParsesItAndPrintsLowercase()
    {
        var result = EntryId.From(Canonical.ToUpperInvariant());

        result.IsSuccess.ShouldBeTrue();
        result.Value.ToString().ShouldBe(Canonical);
    }

    [Fact]
    public void Value_OfTheDefaultInstance_Throws()
    {
        var unset = default(EntryId);

        Should.Throw<InvalidOperationException>(() => unset.Value);
    }

    [Fact]
    public void Value_OfTheParameterlessConstructor_Throws()
    {
        var unset = new EntryId();

        Should.Throw<InvalidOperationException>(() => unset.Value);
    }

    [Fact]
    public void ToString_OfTheDefaultInstance_DoesNotThrow()
    {
        default(EntryId).ToString().ShouldBe(Guid.Empty.ToString("D"));
    }

    [Fact]
    public void Equals_ForSameGuid_IsTrue()
    {
        var guid = Guid.NewGuid();

        EntryId.From(guid).Value.ShouldBe(EntryId.From(guid).Value);
    }
}
