extern alias LedgerWorker;

using Microsoft.Extensions.Configuration;
using WorkerOptions = LedgerWorker::Ledger.Worker.WorkerOptions;
using WorkerOptionsValidator = LedgerWorker::Ledger.Worker.WorkerOptionsValidator;

namespace Ledger.Api.IntegrationTests.Configuration;

[Trait("Category", "Unit")]
public sealed class WorkerOptionsValidationTests
{
    [Fact]
    public void TheDefaults_AreValidAndMatchTheContract()
    {
        var options = Bind(new Dictionary<string, string?>());

        new WorkerOptionsValidator().Validate(null, options).Succeeded.ShouldBeTrue();
        options.FailureBackoff.MinSeconds.ShouldBe(1);
        options.FailureBackoff.MaxSeconds.ShouldBe(30);
    }

    [Theory]
    [InlineData("MinSeconds", "0")]
    [InlineData("MinSeconds", "61")]
    [InlineData("MaxSeconds", "0")]
    [InlineData("MaxSeconds", "61")]
    public void AValueOutOfRange_IsRefusedAndTheMessageNamesTheKey(string key, string value)
    {
        var options = Bind(new Dictionary<string, string?> { [$"Worker:FailureBackoff:{key}"] = value });

        var result = new WorkerOptionsValidator().Validate(null, options);

        result.Failed.ShouldBeTrue();
        (result.Failures ?? []).ShouldAllBe(failure => failure.StartsWith("Worker:FailureBackoff", StringComparison.Ordinal));
        string.Join(' ', result.Failures ?? []).ShouldContain(key);
    }

    [Fact]
    public void AMinimumAboveTheMaximum_IsRefused()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Worker:FailureBackoff:MinSeconds"] = "20",
            ["Worker:FailureBackoff:MaxSeconds"] = "10"
        });

        var result = new WorkerOptionsValidator().Validate(null, options);

        result.Failed.ShouldBeTrue();
        string.Join(' ', result.Failures ?? []).ShouldContain("MinSeconds");
    }

    private static WorkerOptions Bind(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        return configuration.GetSection(WorkerOptions.SectionName).Get<WorkerOptions>() ?? new WorkerOptions();
    }
}
