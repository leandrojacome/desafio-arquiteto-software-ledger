using Ledger.Application.Integrity;
using Ledger.Application.Outbox;
using Ledger.Domain.Accounts;
using Ledger.Infrastructure.Tests.Observability.Support;

namespace Ledger.Infrastructure.Tests.Observability;

[Trait("Category", "Unit")]
public sealed class SpanAttributesPrivacyTests : IDisposable
{
    private static readonly string[] Canaries =
    [
        "Pix enviado canary",
        "E18236120202610011403s0a1b2c3d4e",
        "qs-debit-0001",
        "123.456.789-09",
        "12345678909",
        "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl",
        "Bearer canary-token",
        "80.00",
        "920.00",
        "payload-canary-value",
        "holder-document-canary",
        "cursor-canary-value"
    ];

    private readonly TestTelemetry _telemetry = new();

    public void Dispose()
    {
        _telemetry.Dispose();
    }

    [Fact]
    public void EverySpanOfTheLedger_CarriesNoCanaryValue()
    {
        using var request = TestActivities.Start("http.request");
        request.SetTag("ledger.client_id", "pix-gateway");

        foreach (var type in new[] { "credit", "debit", "reversal" })
        {
            using var operation = _telemetry.Entries.Begin(type);
            operation.Recorded(type);
        }

        using (var operation = _telemetry.Entries.Begin("debit"))
        {
            operation.Rejected("insufficient_funds");
        }

        using (_telemetry.Reads.BeginBalance("as_of"))
        {
        }

        using (var statement = _telemetry.Reads.BeginStatement(50))
        {
            statement.Returned(50, true);
        }

        using (var operation = _telemetry.Security.BeginCreateAccount())
        {
            operation.Outcome("created");
        }

        using (var operation = _telemetry.Security.BeginRewrapBatch())
        {
            operation.Complete(10, 1);
        }

        using (var poll = _telemetry.OutboxPort.BeginPoll())
        {
            poll.BatchSize(10);
        }

        using (var publish = _telemetry.OutboxPort.BeginPublish(Envelope()))
        {
            publish.Confirmed();
        }

        using (var run = _telemetry.IntegrityPort.BeginRun(IntegrityMode.Recent))
        {
            run.AccountsChecked(10);
            run.Violation(IntegrityCheck.ChainGap);
            run.Completed(false);
        }

        _telemetry.Capture.Activities.Count.ShouldBeGreaterThanOrEqualTo(10);
        LeakScanner.Find(LeakScanner.TextOf(_telemetry.Capture), Canaries).ShouldBeEmpty();
    }

    [Fact]
    public void EveryAttributeOfTheLedger_IsOfASimpleTypeAndNotAMonetaryValue()
    {
        using (var operation = _telemetry.Entries.Begin("debit"))
        {
            operation.Recorded("debit");
        }

        using (var statement = _telemetry.Reads.BeginStatement(50))
        {
            statement.Returned(50, false);
        }

        foreach (var activity in _telemetry.Capture.Activities)
        {
            foreach (var tag in activity.TagObjects)
            {
                tag.Value.ShouldNotBeNull();
                (tag.Value is string or bool or int or long).ShouldBeTrue($"{tag.Key} is {tag.Value.GetType()}");
                tag.Key.ShouldNotContain("amount");
                tag.Key.ShouldNotContain("balance");
                tag.Key.ShouldNotContain("description");
                tag.Key.ShouldNotContain("document");
            }
        }
    }

    [Fact]
    public void AttributeNames_OfTheLedgerSpans_AreTheDocumentedOnes()
    {
        using (var operation = _telemetry.Entries.Begin("credit"))
        {
            operation.Recorded("credit");
        }

        using (_telemetry.Reads.BeginBalance("current"))
        {
        }

        using (var statement = _telemetry.Reads.BeginStatement(10))
        {
            statement.Returned(1, false);
        }

        var names = _telemetry.Capture.Activities
            .SelectMany(activity => activity.TagObjects.Select(tag => tag.Key))
            .Distinct()
            .Order()
            .ToList();

        names.ShouldBe(
        [
            "ledger.balance.mode",
            "ledger.entry.type",
            "ledger.idempotent_replay",
            "ledger.outcome",
            "ledger.statement.has_next",
            "ledger.statement.limit",
            "ledger.statement.returned"
        ]);
    }

    private static OutboxEnvelope Envelope() =>
        new(
            Guid.Parse("0192b7c4-5d12-7c88-a0e4-9d3b6f1a2c75"),
            AccountId.From(Guid.Parse("0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33")).Value,
            "EntryRegistered",
            "{\"description\":\"payload-canary-value\",\"amount\":\"80.00\"}",
            "9f3c1a7e2b4d4f60a1c8e5d7b3a29f10",
            "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
            DateTimeOffset.UnixEpoch,
            1);
}
