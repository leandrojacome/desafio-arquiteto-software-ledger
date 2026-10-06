using Ledger.Api.IntegrationTests.Infrastructure;

namespace Ledger.Api.IntegrationTests.Consumers;

[Trait("Category", "Unit")]
public sealed class ReferenceConsumerVersionRulesTests
{
    [Theory]
    [InlineData(1843L, 1843L)]
    [InlineData(1843L, 1842L)]
    [InlineData(1846L, 1L)]
    [InlineData(0L, 0L)]
    public void AnOldOrRepeatedVersion_IsIgnored(long last, long received)
    {
        EventVersionRule.Evaluate(last, received).ShouldBe(new VersionOutcome(VersionDecision.Ignore, 0, 0));
    }

    [Theory]
    [InlineData(0L, 1L)]
    [InlineData(1843L, 1844L)]
    public void TheNextVersion_IsAppliedWithoutGap(long last, long received)
    {
        EventVersionRule.Evaluate(last, received).ShouldBe(new VersionOutcome(VersionDecision.Apply, 0, 0));
    }

    [Fact]
    public void AVersionBeyondTheNext_IsAppliedAndTheMissingVersionsAreReported()
    {
        EventVersionRule.Evaluate(1843, 1846).ShouldBe(new VersionOutcome(VersionDecision.ApplyWithGap, 1844, 1845));
    }

    [Fact]
    public void ThePrefixOfAnUnknownAccountWithAHighVersion_IsAGapFromTheFirstVersion()
    {
        EventVersionRule.Evaluate(0, 3).ShouldBe(new VersionOutcome(VersionDecision.ApplyWithGap, 1, 2));
    }
}
