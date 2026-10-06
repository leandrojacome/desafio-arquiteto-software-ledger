using System.Diagnostics;
using Ledger.Application.Integrity;
using Ledger.Infrastructure.Tests.Observability.Support;

namespace Ledger.Infrastructure.Tests.Observability;

[Trait("Category", "Unit")]
public sealed class IntegrityTelemetryTests : IDisposable
{
    private readonly TestTelemetry _telemetry = new();

    public void Dispose()
    {
        _telemetry.Dispose();
    }

    [Theory]
    [InlineData(IntegrityMode.Recent, "incremental")]
    [InlineData(IntegrityMode.Full, "full")]
    public void Completed_WithoutFindings_CountsAnOkRunForTheMode(IntegrityMode mode, string label)
    {
        using (var run = _telemetry.IntegrityPort.BeginRun(mode))
        {
            run.AccountsChecked(1200);
            run.Completed(true);
        }

        var runs = _telemetry.Capture.Of("ledger.integrity.check.runs").ShouldHaveSingleItem();
        runs.Value.ShouldBe(1);
        runs.Unit.ShouldBe("{run}");
        runs.Tags.Keys.OrderBy(key => key).ShouldBe(["mode", "result"]);
        runs.Has(("mode", label), ("result", "ok")).ShouldBeTrue();
    }

    [Fact]
    public void Completed_WithFindings_CountsAViolationRun()
    {
        using (var run = _telemetry.IntegrityPort.BeginRun(IntegrityMode.Recent))
        {
            run.Completed(false);
        }

        _telemetry.Capture.Count("ledger.integrity.check.runs", ("mode", "incremental"), ("result", "violation"))
            .ShouldBe(1);
    }

    [Fact]
    public void Failed_CountsAnErrorRunAndMarksTheSpanAsError()
    {
        using (var run = _telemetry.IntegrityPort.BeginRun(IntegrityMode.Full))
        {
            run.Failed();
        }

        _telemetry.Capture.Count("ledger.integrity.check.runs", ("mode", "full"), ("result", "error")).ShouldBe(1);
        _telemetry.Capture.SingleActivity("integrity.check").Status.ShouldBe(ActivityStatusCode.Error);
    }

    [Fact]
    public void Run_ThatNeverReportsAResult_CountsNothingAndObservesNoDuration()
    {
        using (_telemetry.IntegrityPort.BeginRun(IntegrityMode.Recent))
        {
        }

        _telemetry.Capture.Of("ledger.integrity.check.runs").ShouldBeEmpty();
        _telemetry.Capture.Of("ledger.integrity.check.duration").ShouldBeEmpty();
    }

    [Fact]
    public void Completed_ThenFailed_CountsOnlyTheFirstResult()
    {
        using (var run = _telemetry.IntegrityPort.BeginRun(IntegrityMode.Recent))
        {
            run.Completed(true);
            run.Failed();
        }

        _telemetry.Capture.Of("ledger.integrity.check.runs").ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData(IntegrityCheck.HeadBalance, "balance_mismatch")]
    [InlineData(IntegrityCheck.HeadVersion, "balance_mismatch")]
    [InlineData(IntegrityCheck.HeadLastEntry, "balance_mismatch")]
    [InlineData(IntegrityCheck.HeadFloor, "balance_mismatch")]
    [InlineData(IntegrityCheck.SumBalance, "balance_mismatch")]
    [InlineData(IntegrityCheck.ChainDrift, "chain_broken")]
    [InlineData(IntegrityCheck.ChainGap, "chain_broken")]
    [InlineData(IntegrityCheck.ChainNonMonotonic, "chain_broken")]
    public void Violation_CountsOneFindingWithTheKindOfTheCheck(IntegrityCheck check, string kind)
    {
        using (var run = _telemetry.IntegrityPort.BeginRun(IntegrityMode.Recent))
        {
            run.Violation(check);
        }

        var violation = _telemetry.Capture.Of("ledger.integrity.violations").ShouldHaveSingleItem();
        violation.Value.ShouldBe(1);
        violation.Unit.ShouldBe("{violation}");
        violation.Tags.Keys.ShouldBe(["kind"]);
        violation.Tag("kind").ShouldBe(kind);
    }

    [Fact]
    public void Dispose_AfterAResult_ObservesTheRunDurationInSecondsByMode()
    {
        using (var run = _telemetry.IntegrityPort.BeginRun(IntegrityMode.Full))
        {
            _telemetry.Time.Advance(TimeSpan.FromMinutes(9));
            run.Completed(true);
        }

        var duration = _telemetry.Capture.Of("ledger.integrity.check.duration").ShouldHaveSingleItem();
        duration.Unit.ShouldBe("s");
        duration.Value.ShouldBe(540, 0.001);
        duration.Tags.Keys.ShouldBe(["mode"]);
        duration.Tag("mode").ShouldBe("full");
    }

    [Fact]
    public void LastSuccessTimestamp_AdvancesOnlyOnACleanRun()
    {
        var firstClean = _telemetry.Time.GetUtcNow();
        using (var run = _telemetry.IntegrityPort.BeginRun(IntegrityMode.Recent))
        {
            run.Completed(true);
        }

        _telemetry.Time.Advance(TimeSpan.FromMinutes(5));
        using (var run = _telemetry.IntegrityPort.BeginRun(IntegrityMode.Recent))
        {
            run.Violation(IntegrityCheck.ChainGap);
            run.Completed(false);
        }

        _telemetry.Time.Advance(TimeSpan.FromMinutes(5));
        using (var run = _telemetry.IntegrityPort.BeginRun(IntegrityMode.Recent))
        {
            run.Failed();
        }

        var gauge = _telemetry.Capture.Observe("ledger.integrity.last.success.timestamp").ShouldHaveSingleItem();
        gauge.Unit.ShouldBe("s");
        gauge.Value.ShouldBe(firstClean.ToUnixTimeSeconds());
        gauge.Tag("mode").ShouldBe("incremental");
    }

    [Fact]
    public void LastSuccessTimestamp_IsKeptPerMode()
    {
        using (var run = _telemetry.IntegrityPort.BeginRun(IntegrityMode.Recent))
        {
            run.Completed(true);
        }

        _telemetry.Time.Advance(TimeSpan.FromHours(1));
        using (var run = _telemetry.IntegrityPort.BeginRun(IntegrityMode.Full))
        {
            run.Completed(true);
        }

        var gauges = _telemetry.Capture.Observe("ledger.integrity.last.success.timestamp");

        gauges.Count.ShouldBe(2);
        (gauges.Single(gauge => gauge.Tag("mode") == "full").Value
         - gauges.Single(gauge => gauge.Tag("mode") == "incremental").Value).ShouldBe(3600);
    }

    [Fact]
    public void LastSuccessTimestamp_BeforeAnyCleanRun_EmitsNothing()
    {
        _telemetry.Capture.Observe("ledger.integrity.last.success.timestamp").ShouldBeEmpty();
    }

    [Fact]
    public void BeginRun_OpensAnInternalSpanWithModeAndAccountsChecked()
    {
        using (var run = _telemetry.IntegrityPort.BeginRun(IntegrityMode.Recent))
        {
            run.AccountsChecked(77);
            run.Completed(true);
        }

        var span = _telemetry.Capture.SingleActivity("integrity.check");
        span.Kind.ShouldBe(ActivityKind.Internal);
        span.GetTagItem("integrity.mode").ShouldBe("incremental");
        span.GetTagItem("integrity.accounts_checked").ShouldBe(77L);
    }
}
