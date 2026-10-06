using Ledger.Infrastructure.Observability;

namespace Ledger.Infrastructure.Tests.Observability;

[Trait("Category", "Unit")]
public sealed class BuildInfoTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2.3+4f3a2b1", "1.2.3")]
    [InlineData("1.2.3-rc.1+4f3a2b1", "1.2.3-rc.1")]
    [InlineData("+4f3a2b1", "0.0.0")]
    [InlineData("", "0.0.0")]
    [InlineData("   ", "0.0.0")]
    [InlineData(null, "0.0.0")]
    public void Normalize_KeepsTheVersionAndDropsTheBuildMetadata(string? informational, string expected)
    {
        BuildInfo.Normalize(informational).ShouldBe(expected);
    }

    [Fact]
    public void Version_IsNeverEmptyAndHasNoBuildMetadata()
    {
        BuildInfo.Version.ShouldNotBeNullOrWhiteSpace();
        BuildInfo.Version.ShouldNotContain("+");
    }
}
