using Ledger.Api.Validation;
using Microsoft.AspNetCore.Http;

namespace Ledger.Api.IntegrationTests.Writes.Validation;

[Trait("Category", "Unit")]
public sealed class IdempotencyKeyReaderTests
{
    private static IdempotencyKeyReading Read(params string[] values)
    {
        var context = new DefaultHttpContext();

        if (values.Length > 0)
        {
            context.Request.Headers["Idempotency-Key"] = values;
        }

        return IdempotencyKeyReader.Read(context.Request.Headers);
    }

    [Fact]
    public void WithoutTheHeader_TheKeyIsMissing()
    {
        var reading = Read();

        reading.IsMissing.ShouldBeTrue();
        reading.Issue.ShouldBeNull();
    }

    [Fact]
    public void WithAnEmptyHeader_TheKeyIsMissing()
    {
        var reading = Read(string.Empty);

        reading.IsMissing.ShouldBeTrue();
        reading.Issue.ShouldBeNull();
    }

    [Theory]
    [InlineData("a")]
    [InlineData("E18236120202610011403s0a1b2c3d4e")]
    [InlineData("rev-4f1a9c2e-7b3d-4e58-9a06-c2d81f5b7e30")]
    [InlineData("!~")]
    public void WithVisibleAsciiOfValidLength_TheKeyIsAccepted(string value)
    {
        var reading = Read(value);

        reading.IsMissing.ShouldBeFalse();
        reading.Issue.ShouldBeNull();
        reading.Key.Value.ShouldBe(value);
    }

    [Fact]
    public void WithOneHundredTwentyEightCharacters_TheKeyIsAccepted()
    {
        Read(new string('k', 128)).Issue.ShouldBeNull();
    }

    [Fact]
    public void WithOneHundredTwentyNineCharacters_TheKeyIsTooLong()
    {
        var issue = Read(new string('k', 129)).Issue.ShouldNotBeNull();

        issue.Field.ShouldBe("Idempotency-Key");
        issue.Reason.ShouldBe("TOO_LONG");
    }

    [Theory]
    [InlineData("with space")]
    [InlineData("aç")]
    [InlineData("tab\there")]
    [InlineData(" leading")]
    public void WithSpaceOrNonVisibleOrNonAsciiCharacter_TheKeyIsAnInvalidFormat(string value)
    {
        var issue = Read(value).Issue.ShouldNotBeNull();

        issue.Field.ShouldBe("Idempotency-Key");
        issue.Reason.ShouldBe("INVALID_FORMAT");
    }

    [Fact]
    public void WithTheHeaderRepeated_TheKeyIsAnInvalidFormat()
    {
        var issue = Read("first-key", "second-key").Issue.ShouldNotBeNull();

        issue.Field.ShouldBe("Idempotency-Key");
        issue.Reason.ShouldBe("INVALID_FORMAT");
    }

    [Fact]
    public void RejectedKey_NeverCarriesTheValueInTheMessage()
    {
        var issue = Read("secret key with spaces").Issue.ShouldNotBeNull();

        issue.Message.ShouldNotContain("secret");
    }

    [Fact]
    public void ReadingWithoutAKey_CannotExposeOne()
    {
        Should.Throw<InvalidOperationException>(() => Read().Key);
    }
}
