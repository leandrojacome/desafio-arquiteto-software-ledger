using System.Text;
using Ledger.Api.Validation;

namespace Ledger.Api.IntegrationTests.Writes.Validation;

[Trait("Category", "Unit")]
public sealed class ReverseEntryRequestReaderTests
{
    private static ReadResult<ReverseEntryInput> Read(string json) =>
        ReverseEntryRequestReader.Read(Encoding.UTF8.GetBytes(json));

    private static (string Field, string Reason) Single(ReadResult<ReverseEntryInput> result)
    {
        result.IsValid.ShouldBeFalse();
        var issue = result.Issues.ShouldHaveSingleItem();

        return (issue.Field, issue.Reason);
    }

    [Fact]
    public void EmptyBody_IsReadAsNoDescription()
    {
        var result = ReverseEntryRequestReader.Read([]);

        result.IsValid.ShouldBeTrue();
        result.Value.Description.ShouldBeNull();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"description\":null}")]
    [InlineData("{\"description\":\"\"}")]
    [InlineData("{\"description\":\"    \"}")]
    public void ObjectWithoutUsefulDescription_IsReadAsNoDescription(string json)
    {
        var result = Read(json);

        result.IsValid.ShouldBeTrue();
        result.Value.Description.ShouldBeNull();
    }

    [Fact]
    public void Description_IsReadTrimmed()
    {
        var result = Read("{\"description\":\"  Cobran\\u00e7a duplicada \"}");

        result.IsValid.ShouldBeTrue();
        result.Value.Description.ShouldBe("Cobrança duplicada");
    }

    [Fact]
    public void Description_WithOneHundredFortyCharacters_IsAccepted()
    {
        Read($"{{\"description\":\"{new string('d', 140)}\"}}").IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Description_WithOneHundredFortyOneCharacters_IsTooLong()
    {
        Single(Read($"{{\"description\":\"{new string('d', 141)}\"}}")).ShouldBe(("description", "TOO_LONG"));
    }

    [Fact]
    public void Description_WithAControlCharacter_IsAnInvalidFormat()
    {
        Single(Read("{\"description\":\"a\\u0007b\"}")).ShouldBe(("description", "INVALID_FORMAT"));
    }

    [Fact]
    public void Description_AsANumber_IsAnInvalidFormat()
    {
        Single(Read("{\"description\":5}")).ShouldBe(("description", "INVALID_FORMAT"));
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("reference")]
    [InlineData("currency")]
    [InlineData("type")]
    [InlineData("occurredAt")]
    public void AnyOtherProperty_IsUnknown(string name)
    {
        Single(Read($"{{\"{name}\":\"x\"}}")).ShouldBe((name, "UNKNOWN_FIELD"));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("12")]
    [InlineData("{ broken")]
    [InlineData("   ")]
    [InlineData("{\"description\":\"a\",\"description\":\"b\"}")]
    public void BodyThatIsNotASingleObject_IsOneInvalidJsonIssueOnTheRoot(string json)
    {
        Single(Read(json)).ShouldBe(("$", "INVALID_JSON"));
    }

    [Fact]
    public void DescriptionAndUnknownProperty_AreReportedTogether()
    {
        var result = Read($"{{\"description\":\"{new string('d', 141)}\",\"amount\":\"1.00\"}}");

        result.Issues.Select(issue => (issue.Field, issue.Reason)).ShouldBe(
            [("description", "TOO_LONG"), ("amount", "UNKNOWN_FIELD")]);
    }
}
