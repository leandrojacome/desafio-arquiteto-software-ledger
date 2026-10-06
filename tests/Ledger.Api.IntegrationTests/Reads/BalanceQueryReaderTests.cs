using Ledger.Api.Validation;
using Ledger.Domain.Accounts;

namespace Ledger.Api.IntegrationTests.Reads;

[Trait("Category", "Unit")]
public sealed class BalanceQueryReaderTests
{
    private const string UnknownFieldMessage = "O parâmetro não é suportado.";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("?")]
    public void Read_WithoutAsOf_IsTheCurrentBalance(string? queryString)
    {
        var read = BalanceQueryReader.Read(queryString);

        read.IsValid.ShouldBeTrue();
        read.AsOf.ShouldBeNull();
    }

    [Theory]
    [InlineData("?asOf=2026-10-01T14:03:11Z", 0)]
    [InlineData("asOf=2026-10-01T14:03:11.482913Z", 4_829_130)]
    [InlineData("?asOf=2026-10-01T14:03:11%2B00:00", 0)]
    [InlineData("?asOf=2026-10-01T11:03:11-03:00", 0)]
    [InlineData("?asOf=2026-10-01T11:03:11.482913-03:00", 4_829_130)]
    [InlineData("?asOf=2026-10-01T14%3A03%3A11Z", 0)]
    [InlineData("?as%4Ff=2026-10-01T14:03:11Z", 0)]
    public void Read_WithAValidAsOf_ReturnsTheInstant(string queryString, long extraTicks)
    {
        var read = BalanceQueryReader.Read(queryString);

        read.IsValid.ShouldBeTrue();
        read.AsOf.ShouldBe(new DateTimeOffset(2026, 10, 1, 14, 3, 11, TimeSpan.Zero).AddTicks(extraTicks));
        read.AsOf.ShouldNotBeNull().Offset.ShouldBe(TimeSpan.Zero);
    }

    [Theory]
    [InlineData("?asOf=")]
    [InlineData("?asOf")]
    [InlineData("?asOf=abc")]
    [InlineData("?asOf=2026-10-01T14:03:11")]
    [InlineData("?asOf=2026-10-01T11:03:11-00:00")]
    [InlineData("?asOf=2026-10-01T11:03:11-15:00")]
    [InlineData("?asOf=2026-10-01T14:03:11+00:00")]
    [InlineData("?asOf=2026-10-01T14:03:11Z&asOf=2026-10-01T14:03:11Z")]
    [InlineData("?asOf=a&asOf=b")]
    public void Read_WithAnInvalidOrRepeatedAsOf_IsRejectedWithTheInvalidAsOfError(string queryString)
    {
        var read = BalanceQueryReader.Read(queryString);

        read.IsValid.ShouldBeFalse();
        read.Error.ShouldBe(BalanceErrors.InvalidAsOf);
        read.Issues.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("?asof=2026-10-01T14:03:11Z", "asof")]
    [InlineData("?AsOf=2026-10-01T14:03:11Z", "AsOf")]
    [InlineData("?as_of=2026-10-01T14:03:11Z", "as_of")]
    [InlineData("?from=2026-10-01T14:03:11Z", "from")]
    [InlineData("?foo=1", "foo")]
    public void Read_WithAnUnknownName_ReportsItAsUnknownField(string queryString, string expectedField)
    {
        var read = BalanceQueryReader.Read(queryString);

        read.IsValid.ShouldBeFalse();
        read.Error.ShouldBeNull();

        var issue = read.Issues.ShouldHaveSingleItem();

        issue.Field.ShouldBe(expectedField);
        issue.Reason.ShouldBe("UNKNOWN_FIELD");
        issue.Message.ShouldBe(UnknownFieldMessage);
    }

    [Fact]
    public void Read_WithTheSameUnknownNameTwice_ReportsOneItem()
    {
        var read = BalanceQueryReader.Read("?foo=1&foo=2&bar=3");

        read.Issues.Select(issue => issue.Field).ShouldBe(["foo", "bar"]);
    }

    [Theory]
    [InlineData("?a%0Ab=1")]
    [InlineData("?a%20b=1")]
    [InlineData("?%D9%A2%D9%A0=1")]
    [InlineData("?=1")]
    [InlineData("?<script>=1")]
    public void Read_WithAnUnknownNameOutsideTheSafeAlphabet_ReportsItAsUnknown(string queryString)
    {
        var read = BalanceQueryReader.Read(queryString);

        read.Issues.ShouldHaveSingleItem().Field.ShouldBe("(unknown)");
    }

    [Fact]
    public void Read_WithANameLongerThanSixtyFourCharacters_ReportsItAsUnknown()
    {
        var read = BalanceQueryReader.Read($"?{new string('a', 65)}=1");

        read.Issues.ShouldHaveSingleItem().Field.ShouldBe("(unknown)");
    }

    [Fact]
    public void Read_WithAnUnknownNameAndAnInvalidAsOf_ReportsOnlyTheUnknownName()
    {
        var read = BalanceQueryReader.Read("?foo=1&asOf=abc");

        read.Error.ShouldBeNull();
        read.Issues.ShouldHaveSingleItem().Field.ShouldBe("foo");
    }

    [Fact]
    public void Read_NeverEchoesTheValueReceived()
    {
        var read = BalanceQueryReader.Read("?asOf=SENTINEL-VALUE-91&sentinel-name-91=SENTINEL-VALUE-92");

        var text = string.Join(' ', read.Issues.Select(issue => $"{issue.Field} {issue.Reason} {issue.Message}"));

        text.ShouldNotContain("SENTINEL");
        text.ShouldNotContain("sentinel");
    }
}
