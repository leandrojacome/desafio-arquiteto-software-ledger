using Microsoft.Extensions.Options;

namespace Ledger.Api.IntegrationTests.Configuration;

[Trait("Category", "Unit")]
public sealed class StartupFailureReporterTests
{
    private static OptionsValidationException Validation(params string[] failures) =>
        new(string.Empty, typeof(object), failures);

    [Fact]
    public void FailuresOf_AnOptionsValidationException_ReturnsItsFailures()
    {
        var failures = StartupFailureReporter.FailuresOf(Validation("Postgres:SslMode: must be VerifyFull."));

        failures.ShouldBe(["Postgres:SslMode: must be VerifyFull."]);
    }

    [Fact]
    public void FailuresOf_AnAggregateOfOptionsFailures_MergesAllOfThem()
    {
        var aggregate = new AggregateException(
            Validation("Postgres:SslMode: must be VerifyFull."),
            Validation("Authentication:Mode LocalKey is not allowed.", "RateLimiting:Enabled: must be true."));

        StartupFailureReporter.FailuresOf(aggregate).ShouldBe(
        [
            "Postgres:SslMode: must be VerifyFull.",
            "Authentication:Mode LocalKey is not allowed.",
            "RateLimiting:Enabled: must be true."
        ]);
    }

    [Fact]
    public void FailuresOf_AnAggregateInsideAnAggregate_IsFlattened()
    {
        var nested = new AggregateException(new AggregateException(Validation("A: failed.")), Validation("B: failed."));

        StartupFailureReporter.FailuresOf(nested).ShouldBe(["A: failed.", "B: failed."]);
    }

    [Fact]
    public void FailuresOf_AnAggregateWithAnyOtherException_IsNotAConfigurationFailure()
    {
        var aggregate = new AggregateException(Validation("A: failed."), new InvalidOperationException("a port is missing"));

        StartupFailureReporter.FailuresOf(aggregate).ShouldBeNull();
    }

    [Fact]
    public void FailuresOf_AnyOtherException_IsNotAConfigurationFailure()
    {
        StartupFailureReporter.FailuresOf(new TimeoutException("the database did not answer")).ShouldBeNull();
    }

    [Fact]
    public void OwnsTheProcess_InsideTheTestHost_IsFalseSoTheExceptionsReachTheCaller()
    {
        StartupFailureReporter.OwnsTheProcess.ShouldBeFalse();
    }

    [Fact]
    public void TheExitCode_IsThreeLikeTheWorker()
    {
        StartupFailureReporter.InvalidConfigurationExitCode.ShouldBe(3);
    }
}
