using Ledger.Domain.Accounts;

namespace Ledger.Domain.Tests.Accounts;

[Trait("Category", "Unit")]
public sealed class AccountIdTests
{
    private const string Canonical = "0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33";

    [Fact]
    public void From_WithValidGuid_ReturnsAccountId()
    {
        var guid = Guid.Parse(Canonical);

        var result = AccountId.From(guid);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(guid);
    }

    [Fact]
    public void From_WithEmptyGuid_ReturnsNotFound()
    {
        var result = AccountId.From(Guid.Empty);

        result.Error.ShouldBe(AccountErrors.NotFound);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("0192b7c2-81aa-7e04-b1d5")]
    [InlineData("zzzzzzzz-zzzz-zzzz-zzzz-zzzzzzzzzzzz")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void From_WithTextThatIsNotAGuid_ReturnsNotFound(string? text)
    {
        var result = AccountId.From(text);

        result.Error.ShouldBe(AccountErrors.NotFound);
    }

    [Theory]
    [InlineData("0192b7c281aa7e04b1d56f0c2a9e8d33")]
    [InlineData("{0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33}")]
    [InlineData("(0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33)")]
    [InlineData(" 0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33")]
    [InlineData("0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33 ")]
    [InlineData("{0x0192b7c2,0x81aa,0x7e04,{0xb1,0xd5,0x6f,0x0c,0x2a,0x9e,0x8d,0x33}}")]
    [InlineData("+192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33")]
    [InlineData("0x92b7c2-81aa-7e04-b1d5-6f0c2a9e8d33")]
    [InlineData("0192b7c2-0xaa-7e04-b1d5-6f0c2a9e8d33")]
    public void From_WithAnyFormatOtherThanHyphenated_ReturnsNotFound(string text)
    {
        var result = AccountId.From(text);

        result.Error.ShouldBe(AccountErrors.NotFound);
    }

    [Fact]
    public void From_WithGuidText_ParsesIt()
    {
        var result = AccountId.From(Canonical);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ToString().ShouldBe(Canonical);
    }

    [Fact]
    public void From_WithUppercaseGuidText_ParsesItAndPrintsLowercase()
    {
        var result = AccountId.From(Canonical.ToUpperInvariant());

        result.IsSuccess.ShouldBeTrue();
        result.Value.ToString().ShouldBe(Canonical);
    }

    [Fact]
    public void Value_OfTheDefaultInstance_Throws()
    {
        var unset = default(AccountId);

        Should.Throw<InvalidOperationException>(() => unset.Value);
    }

    [Fact]
    public void Value_OfTheParameterlessConstructor_Throws()
    {
        var unset = new AccountId();

        Should.Throw<InvalidOperationException>(() => unset.Value);
    }

    [Fact]
    public void ToString_OfTheDefaultInstance_DoesNotThrow()
    {
        default(AccountId).ToString().ShouldBe(Guid.Empty.ToString("D"));
    }

    [Fact]
    public void Equals_ForSameGuid_IsTrue()
    {
        var guid = Guid.NewGuid();

        AccountId.From(guid).Value.ShouldBe(AccountId.From(guid).Value);
    }
}
