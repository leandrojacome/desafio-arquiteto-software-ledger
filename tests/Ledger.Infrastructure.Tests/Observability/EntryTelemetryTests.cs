using System.Diagnostics;
using Ledger.Infrastructure.Tests.Observability.Support;

namespace Ledger.Infrastructure.Tests.Observability;

[Trait("Category", "Unit")]
public sealed class EntryTelemetryTests : IDisposable
{
    private const string Client = "pix-gateway";

    private readonly TestTelemetry _telemetry = new();
    private readonly Activity _request = StartRequest(Client);

    public void Dispose()
    {
        _request.Dispose();
        _telemetry.Dispose();
    }

    [Theory]
    [InlineData("credit")]
    [InlineData("debit")]
    [InlineData("reversal")]
    public void Recorded_CountsTheEntryWithTypeAndClient(string type)
    {
        using (var operation = _telemetry.Entries.Begin(type))
        {
            operation.Recorded(type);
        }

        _telemetry.Capture.Count("ledger.entries.recorded", ("type", type), ("client", Client)).ShouldBe(1);
        _telemetry.Capture.Of("ledger.entries.recorded").ShouldHaveSingleItem().Value.ShouldBe(1);
    }

    [Fact]
    public void Dispose_AfterRecorded_ObservesTheElapsedSecondsWithTypeAndOutcome()
    {
        using (var operation = _telemetry.Entries.Begin("debit"))
        {
            operation.Recorded("debit");
            _telemetry.Time.Advance(TimeSpan.FromMilliseconds(120));
        }

        var duration = _telemetry.Capture.Of("ledger.entry.duration").ShouldHaveSingleItem();
        duration.Unit.ShouldBe("s");
        duration.Value.ShouldBe(0.12, 0.0001);
        duration.Has(("type", "debit"), ("outcome", "recorded")).ShouldBeTrue();
        duration.Tags.Keys.OrderBy(key => key).ShouldBe(["outcome", "type"]);
    }

    [Fact]
    public void Replayed_CountsTheReplayAndReportsTheReplayedOutcome()
    {
        using (var operation = _telemetry.Entries.Begin("credit"))
        {
            operation.Replayed();
        }

        _telemetry.Capture.Count("idempotency.replays", ("client", Client)).ShouldBe(1);
        _telemetry.Capture.Of("ledger.entries.recorded").ShouldBeEmpty();
        _telemetry.Capture.Count("ledger.entry.duration", ("type", "credit"), ("outcome", "replayed")).ShouldBe(1);
    }

    [Theory]
    [InlineData("insufficient_funds")]
    [InlineData("currency_mismatch")]
    [InlineData("account_not_found")]
    [InlineData("entry_already_reversed")]
    [InlineData("entry_not_reversible")]
    [InlineData("entry_not_found")]
    [InlineData("validation")]
    public void Rejected_CountsTheReasonWithTheClientAndReportsTheRejectedOutcome(string reason)
    {
        using (var operation = _telemetry.Entries.Begin("debit"))
        {
            operation.Rejected(reason);
        }

        _telemetry.Capture.Count("ledger.entries.rejected", ("reason", reason), ("client", Client)).ShouldBe(1);
        _telemetry.Capture.Count("ledger.entry.duration", ("type", "debit"), ("outcome", "rejected")).ShouldBe(1);
    }

    [Fact]
    public void IdempotencyConflict_CountsTheConflictAndTheRejectionWithoutCountingAReplay()
    {
        using (var operation = _telemetry.Entries.Begin("debit"))
        {
            operation.IdempotencyConflict();
        }

        _telemetry.Capture.Count("idempotency.conflicts", ("client", Client)).ShouldBe(1);
        _telemetry.Capture.Count("ledger.entries.rejected", ("reason", "idempotency_conflict"), ("client", Client))
            .ShouldBe(1);
        _telemetry.Capture.Of("idempotency.replays").ShouldBeEmpty();
        _telemetry.Capture.Count("ledger.entry.duration", ("type", "debit"), ("outcome", "rejected")).ShouldBe(1);
    }

    [Fact]
    public void RecordedAtCorrected_CountsTheCorrectionWithTheClient()
    {
        using (var operation = _telemetry.Entries.Begin("credit"))
        {
            operation.Recorded("credit");
            operation.RecordedAtCorrected();
        }

        _telemetry.Capture.Count("ledger.recorded_at.corrections", ("client", Client)).ShouldBe(1);
    }

    [Fact]
    public void Dispose_WithoutAnyOutcome_ReportsFailedAndMarksTheSpanAsError()
    {
        using (_telemetry.Entries.Begin("debit"))
        {
        }

        _telemetry.Capture.Count("ledger.entry.duration", ("type", "debit"), ("outcome", "failed")).ShouldBe(1);
        var span = _telemetry.Capture.SingleActivity("ledger.record_entry");
        span.Status.ShouldBe(ActivityStatusCode.Error);
        span.GetTagItem("ledger.outcome").ShouldBe("failed");
    }

    [Fact]
    public void Dispose_CalledTwice_ObservesTheDurationOnce()
    {
        var operation = _telemetry.Entries.Begin("credit");
        operation.Recorded("credit");

        operation.Dispose();
        operation.Dispose();

        _telemetry.Capture.Of("ledger.entry.duration").ShouldHaveSingleItem();
        _telemetry.Capture.Activities.Count(activity => activity.OperationName == "ledger.record_entry")
            .ShouldBe(1);
    }

    [Theory]
    [InlineData("credit", "recorded", false)]
    [InlineData("debit", "replayed", true)]
    [InlineData("reversal", "rejected", false)]
    public void Span_CarriesTypeOutcomeAndReplayFlagAsChildOfTheRequest(string type, string outcome, bool replay)
    {
        using (var operation = _telemetry.Entries.Begin(type))
        {
            switch (outcome)
            {
                case "recorded":
                    operation.Recorded(type);
                    break;
                case "replayed":
                    operation.Replayed();
                    break;
                default:
                    operation.Rejected("insufficient_funds");
                    break;
            }
        }

        var span = _telemetry.Capture.SingleActivity("ledger.record_entry");
        span.Kind.ShouldBe(ActivityKind.Internal);
        span.Parent.ShouldBe(_request);
        span.GetTagItem("ledger.entry.type").ShouldBe(type);
        span.GetTagItem("ledger.outcome").ShouldBe(outcome);
        span.GetTagItem("ledger.idempotent_replay").ShouldBe(replay);
        span.Status.ShouldNotBe(ActivityStatusCode.Error);
    }

    [Fact]
    public void Begin_WhenTheRequestCarriesNoClient_LabelsTheClientAsUnknown()
    {
        _request.SetTag("ledger.client_id", null);
        using (var operation = _telemetry.Entries.Begin("credit"))
        {
            operation.Recorded("credit");
        }

        _telemetry.Capture.Count("ledger.entries.recorded", ("type", "credit"), ("client", "unknown")).ShouldBe(1);
    }

    [Theory]
    [InlineData("client with spaces")]
    [InlineData("café")]
    [InlineData("a,b")]
    public void Begin_WhenTheClientIsNotASafeIdentifier_LabelsItAsUnknown(string client)
    {
        _request.SetTag("ledger.client_id", client);

        using (var operation = _telemetry.Entries.Begin("credit"))
        {
            operation.Recorded("credit");
        }

        _telemetry.Capture.Count("ledger.entries.recorded", ("type", "credit"), ("client", "unknown")).ShouldBe(1);
    }

    [Fact]
    public void Begin_WithAnUnknownType_StillReturnsAWorkingOperationWithoutEmittingTypedMetrics()
    {
        using (var operation = _telemetry.Entries.Begin("anything the caller invents"))
        {
            operation.Recorded("another invention");
            operation.Rejected("free text with an account 12345678909");
        }

        _telemetry.Capture.Of("ledger.entries.recorded").ShouldBeEmpty();
        _telemetry.Capture.Of("ledger.entries.rejected").ShouldBeEmpty();
        _telemetry.Capture.Of("ledger.entry.duration").ShouldBeEmpty();
    }

    [Fact]
    public void Rejected_WithAFreeTextReason_NeverBecomesALabel()
    {
        using (var operation = _telemetry.Entries.Begin("debit"))
        {
            operation.Rejected("insufficient funds for account 0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33");
        }

        _telemetry.Capture.Of("ledger.entries.rejected").ShouldBeEmpty();
        _telemetry.Capture.Count("ledger.entry.duration", ("outcome", "rejected")).ShouldBe(1);
    }

    private static Activity StartRequest(string client)
    {
        var request = TestActivities.Start("http.request");
        request.SetTag("ledger.client_id", client);

        return request;
    }
}
