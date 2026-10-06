using System.Diagnostics;
using Ledger.Infrastructure.Tests.Observability.Support;

namespace Ledger.Infrastructure.Tests.Observability;

[Trait("Category", "Unit")]
public sealed class ReadTelemetryTests : IDisposable
{
    private readonly TestTelemetry _telemetry = new();
    private readonly Activity _request = TestActivities.Start("http.request");

    public void Dispose()
    {
        _request.Dispose();
        _telemetry.Dispose();
    }

    [Theory]
    [InlineData("current")]
    [InlineData("as_of")]
    public void BeginBalance_OnDispose_ObservesTheElapsedSecondsWithTheMode(string mode)
    {
        using (_telemetry.Reads.BeginBalance(mode))
        {
            _telemetry.Time.Advance(TimeSpan.FromMilliseconds(35));
        }

        var duration = _telemetry.Capture.Of("ledger.balance.query.duration").ShouldHaveSingleItem();
        duration.Unit.ShouldBe("s");
        duration.Value.ShouldBe(0.035, 0.0001);
        duration.Tags.ShouldContainKeyAndValue("mode", mode);
        duration.Tags.Count.ShouldBe(1);
    }

    [Fact]
    public void BeginBalance_OpensASpanWithTheModeAsChildOfTheRequest()
    {
        using (_telemetry.Reads.BeginBalance("as_of"))
        {
        }

        var span = _telemetry.Capture.SingleActivity("ledger.balance_query");
        span.Kind.ShouldBe(ActivityKind.Internal);
        span.Parent.ShouldBe(_request);
        span.GetTagItem("ledger.balance.mode").ShouldBe("as_of");
    }

    [Fact]
    public void BeginBalance_WithAnUnknownMode_ObservesNothingAndStillWorks()
    {
        using (_telemetry.Reads.BeginBalance("last tuesday"))
        {
        }

        _telemetry.Capture.Of("ledger.balance.query.duration").ShouldBeEmpty();
        _telemetry.Capture.SingleActivity("ledger.balance_query").GetTagItem("ledger.balance.mode").ShouldBeNull();
    }

    [Fact]
    public void BeginBalance_WhenDisposedTwice_ObservesOnce()
    {
        var operation = _telemetry.Reads.BeginBalance("current");

        operation.Dispose();
        operation.Dispose();

        _telemetry.Capture.Of("ledger.balance.query.duration").ShouldHaveSingleItem();
    }

    [Fact]
    public void BeginStatement_RecordsLimitReturnedAndHasNextOnTheSpan()
    {
        using (var operation = _telemetry.Reads.BeginStatement(50))
        {
            operation.Returned(50, true);
        }

        var span = _telemetry.Capture.SingleActivity("ledger.statement_query");
        span.Kind.ShouldBe(ActivityKind.Internal);
        span.Parent.ShouldBe(_request);
        span.GetTagItem("ledger.statement.limit").ShouldBe(50);
        span.GetTagItem("ledger.statement.returned").ShouldBe(50);
        span.GetTagItem("ledger.statement.has_next").ShouldBe(true);
    }

    [Fact]
    public void BeginStatement_WhenNothingIsReturned_LeavesTheOutcomeTagsOff()
    {
        using (_telemetry.Reads.BeginStatement(10))
        {
        }

        var span = _telemetry.Capture.SingleActivity("ledger.statement_query");
        span.GetTagItem("ledger.statement.returned").ShouldBeNull();
        span.GetTagItem("ledger.statement.has_next").ShouldBeNull();
    }

    [Fact]
    public void BeginStatement_DoesNotCreateAHistogramOfItsOwn()
    {
        using (var operation = _telemetry.Reads.BeginStatement(10))
        {
            operation.Returned(3, false);
        }

        _telemetry.Capture.All.ShouldBeEmpty();
    }
}
