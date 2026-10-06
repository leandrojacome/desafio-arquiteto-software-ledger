extern alias LedgerWorker;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using IntegrityOptions = LedgerWorker::Ledger.Worker.IntegrityOptions;
using IntegrityOptionsValidator = LedgerWorker::Ledger.Worker.IntegrityOptionsValidator;

namespace Ledger.Api.IntegrationTests.Configuration;

[Trait("Category", "Unit")]
public sealed class IntegrityOptionsValidationTests
{
    [Fact]
    public void TheDefaults_AreValidAndMatchTheContract()
    {
        var options = Bind(new Dictionary<string, string?>());

        new IntegrityOptionsValidator().Validate(null, options).Succeeded.ShouldBeTrue();
        options.RecentIntervalMinutes.ShouldBe(5);
        options.RecentOverlapMinutes.ShouldBe(1);
        options.FullIntervalHours.ShouldBe(24);
        options.HeadBatchSize.ShouldBe(5000);
        options.ChainSliceMinutes.ShouldBe(10);
    }

    [Theory]
    [InlineData("RecentIntervalMinutes", "0")]
    [InlineData("RecentIntervalMinutes", "61")]
    [InlineData("RecentOverlapMinutes", "0")]
    [InlineData("RecentOverlapMinutes", "61")]
    [InlineData("FullIntervalHours", "0")]
    [InlineData("FullIntervalHours", "169")]
    [InlineData("HeadBatchSize", "99")]
    [InlineData("HeadBatchSize", "20001")]
    [InlineData("ChainSliceMinutes", "0")]
    [InlineData("ChainSliceMinutes", "61")]
    public void AValueOutOfRange_IsRefusedAndTheMessageNamesTheKey(string key, string value)
    {
        var options = Bind(new Dictionary<string, string?> { [$"Integrity:{key}"] = value });

        var result = new IntegrityOptionsValidator().Validate(null, options);

        result.Failed.ShouldBeTrue();
        (result.Failures ?? []).ShouldAllBe(failure => failure.StartsWith("Integrity", StringComparison.Ordinal));
        string.Join(' ', result.Failures ?? []).ShouldContain(key);
    }

    [Theory]
    [InlineData("RecentIntervalMinutes", "1")]
    [InlineData("RecentIntervalMinutes", "60")]
    [InlineData("RecentOverlapMinutes", "1")]
    [InlineData("RecentOverlapMinutes", "60")]
    [InlineData("FullIntervalHours", "1")]
    [InlineData("FullIntervalHours", "168")]
    [InlineData("HeadBatchSize", "100")]
    [InlineData("HeadBatchSize", "20000")]
    [InlineData("ChainSliceMinutes", "1")]
    [InlineData("ChainSliceMinutes", "60")]
    public void TheBoundariesOfEachRange_AreAccepted(string key, string value)
    {
        var options = Bind(new Dictionary<string, string?> { [$"Integrity:{key}"] = value });

        new IntegrityOptionsValidator().Validate(null, options).Succeeded.ShouldBeTrue();
    }

    private static IntegrityOptions Bind(IReadOnlyDictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build()
            .GetSection(IntegrityOptions.SectionName)
            .Get<IntegrityOptions>() ?? new IntegrityOptions();
    }
}
