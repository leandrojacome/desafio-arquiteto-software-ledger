using System.Text.RegularExpressions;
using Ledger.Application.Integrity;
using Ledger.Application.Outbox;
using Ledger.Domain.Accounts;
using Ledger.Infrastructure.Observability.Labels;
using Ledger.Infrastructure.Tests.Observability.Support;

namespace Ledger.Infrastructure.Tests.Observability;

[Trait("Category", "Unit")]
public sealed partial class MetricCardinalityTests : IDisposable
{
    private static readonly string[] ForbiddenKeys =
    [
        "account_id", "accountid", "entry_id", "entryid", "correlation_id", "correlationid", "message_id",
        "messageid", "idempotency_key", "idempotencykey", "document", "holder_document", "client_id", "trace_id",
        "request_id", "route", "path", "url", "description", "reference", "amount", "balance"
    ];

    private static readonly string[] HostileTexts =
    [
        "0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33",
        "123.456.789-09",
        "12345678909",
        "Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl",
        "qs-debit-0001",
        "Pix enviado",
        "insufficient funds for account 0192b7c2",
        string.Empty,
        " "
    ];

    private static readonly string[] EntryTypes = ["credit", "debit", "reversal"];

    private static readonly string[] ReadModes = ["current", "as_of"];

    private static readonly string[] UnsafeClients =
    [
        "123.456.789-09",
        "12345678909",
        "Pix enviado",
        "Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl",
        "Password=hunter2",
        string.Empty,
        " "
    ];

    private static readonly string[] AllLoops =
    [
        "outbox", "integrity-recent", "integrity-full", "measure", "prune", "idempotency-prune", "rewrap"
    ];

    private static readonly Dictionary<(string Instrument, string Tag), string[]> ClosedValues = new()
    {
        [("ledger.entries.recorded", "type")] = ["credit", "debit", "reversal"],
        [("ledger.entries.rejected", "reason")] =
        [
            "insufficient_funds", "currency_mismatch", "account_not_found", "idempotency_conflict",
            "entry_already_reversed", "entry_not_reversible", "entry_not_found", "validation"
        ],
        [("ledger.entry.duration", "type")] = ["credit", "debit", "reversal"],
        [("ledger.entry.duration", "outcome")] = ["recorded", "replayed", "rejected", "failed"],
        [("ledger.balance.query.duration", "mode")] = ["current", "as_of"],
        [("ledger.db.command.duration", "operation")] =
        [
            "insert_entry", "update_balance", "insert_outbox", "insert_idempotency_key", "select_balance",
            "select_balance_as_of", "select_entries"
        ],
        [("ledger.db.retries", "reason")] = ["deadlock", "serialization_failure", "transient_connection"],
        [("ledger.auth.failures", "reason")] =
            ["missing_token", "invalid_token", "expired", "insufficient_scope", "missing_client_id", "invalid_client_id"],
        [("ledger.rate_limit.rejections", "policy")] =
        [
            "write-per-client", "read-per-client", "write-per-account", "write-concurrency", "balance-concurrency",
            "statement-concurrency"
        ],
        [("ledger.pii.decrypt", "purpose")] = ["rewrap", "holder_lookup", "investigation"],
        [("ledger.key.reloads", "result")] = ["ok", "failed"],
        [("ledger.audit.recorded", "event_type")] =
        [
            "account.created", "authorization.denied_write", "pii.decrypted", "pii.rewrapped",
            "keys.version_activated"
        ],
        [("ledger.audit.recorded", "outcome")] = ["SUCCESS", "DENIED"],
        [("ledger.audit.skipped", "reason")] = ["rate_capped", "write_failed"],
        [("ledger.rewrap.accounts", "result")] = ["rewrapped", "failed"],
        [("outbox.publish.failures", "reason")] = ["broker_unavailable", "nack", "timeout", "serialization", "unroutable"],
        [("worker.last_cycle.timestamp", "loop")] = AllLoops,
        [("ledger.worker.loop.failures", "loop")] = AllLoops,
        [("ledger.worker.loop.last_success.timestamp", "loop")] = AllLoops,
        [("ledger.integrity.check.runs", "mode")] = ["incremental", "full"],
        [("ledger.integrity.check.runs", "result")] = ["ok", "violation", "error"],
        [("ledger.integrity.violations", "kind")] = ["balance_mismatch", "chain_broken"],
        [("ledger.integrity.last.success.timestamp", "mode")] = ["incremental", "full"],
        [("ledger.integrity.check.duration", "mode")] = ["incremental", "full"]
    };

    private static readonly string[] ClientInstruments =
    [
        "ledger.entries.recorded", "ledger.entries.rejected", "idempotency.replays", "idempotency.conflicts",
        "ledger.recorded_at.corrections"
    ];

    private readonly TestTelemetry _telemetry = new();

    public void Dispose()
    {
        _telemetry.Dispose();
    }

    [Fact]
    public void MixedTraffic_EmitsOnlyKeysAndValuesOfTheClosedSets()
    {
        RunTheMixedTraffic();

        var measurements = _telemetry.Capture.All;
        measurements.ShouldNotBeEmpty();

        foreach (var measurement in measurements)
        {
            foreach (var tag in measurement.Tags)
            {
                var value = tag.Value as string;
                value.ShouldNotBeNull($"{measurement.Instrument}:{tag.Key} is not text");

                if (tag.Key == "client")
                {
                    ClientInstruments.ShouldContain(measurement.Instrument);
                    ClientLabel().IsMatch(value).ShouldBeTrue($"client label '{value}'");

                    continue;
                }

                ClosedValues.TryGetValue((measurement.Instrument, tag.Key), out var allowed)
                    .ShouldBeTrue($"{measurement.Instrument} must not carry the label {tag.Key}");
                allowed.ShouldNotBeNull().ShouldContain(value, $"{measurement.Instrument}:{tag.Key}={value}");
            }
        }
    }

    [Fact]
    public void MixedTraffic_NeverEmitsAnIdentifierLikeLabelKey()
    {
        RunTheMixedTraffic();

        var keys = _telemetry.Capture.All.SelectMany(measurement => measurement.Tags.Keys).Distinct().ToList();

        keys.ShouldNotBeEmpty();
        keys.Intersect(ForbiddenKeys, StringComparer.OrdinalIgnoreCase).ShouldBeEmpty();
    }

    [Fact]
    public void MixedTraffic_NeverTurnsHostileTextIntoAMetricLabel()
    {
        RunTheMixedTraffic();

        var everyValue = _telemetry.Capture.All
            .SelectMany(measurement => measurement.Tags.Values)
            .Select(value => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)
            .ToList();

        foreach (var hostile in HostileTexts.Where(text => text.Trim().Length > 0))
        {
            everyValue.ShouldNotContain(hostile);
        }

        everyValue.ShouldNotContain(string.Empty);
    }

    [Fact]
    public void AuthFailures_NeverCarryTheClient()
    {
        RunTheMixedTraffic();

        _telemetry.Capture.Of("ledger.auth.failures").ShouldNotBeEmpty();
        _telemetry.Capture.Of("ledger.auth.failures").ShouldAllBe(measurement => !measurement.Tags.ContainsKey("client"));
    }

    [Fact]
    public void ClientLabel_OfAHostileClaim_NeverReachesAMetric()
    {
        using var request = TestActivities.Start("http.request");

        foreach (var hostile in UnsafeClients)
        {
            request.SetTag("ledger.client_id", hostile);
            using var operation = _telemetry.Entries.Begin("credit");
            operation.Recorded("credit");
        }

        var clients = _telemetry.Capture.Of("ledger.entries.recorded").Select(measurement => measurement.Tag("client"));

        clients.Distinct().ShouldBe(["unknown"]);
    }

    [Fact]
    public void ClientLabel_UnderAnExplosionOfDistinctClients_StaysBounded()
    {
        using var request = TestActivities.Start("http.request");

        for (var index = 0; index < 1000; index++)
        {
            request.SetTag("ledger.client_id", $"client-{index}");
            using var operation = _telemetry.Entries.Begin("debit");
            operation.Recorded("debit");
        }

        var distinct = _telemetry.Capture.Of("ledger.entries.recorded")
            .Select(measurement => measurement.Tag("client"))
            .Distinct()
            .ToList();

        distinct.Count.ShouldBeLessThanOrEqualTo(129);
        distinct.ShouldContain("other");
    }

    private void RunTheMixedTraffic()
    {
        using var request = TestActivities.Start("http.request");
        request.SetTag("ledger.client_id", "pix-gateway");

        foreach (var type in EntryTypes)
        {
            using (var operation = _telemetry.Entries.Begin(type))
            {
                operation.Recorded(type);
                operation.RecordedAtCorrected();
            }

            using (var operation = _telemetry.Entries.Begin(type))
            {
                operation.Replayed();
            }

            using (var operation = _telemetry.Entries.Begin(type))
            {
                operation.IdempotencyConflict();
            }
        }

        foreach (var reason in LabelTable<RejectionReason>.Texts.Concat(HostileTexts))
        {
            using var operation = _telemetry.Entries.Begin("debit");
            operation.Rejected(reason);
        }

        using (var operation = _telemetry.Entries.Begin("hostile"))
        {
            operation.Recorded(HostileTexts[0]);
        }

        foreach (var mode in ReadModes.Concat(HostileTexts))
        {
            using (_telemetry.Reads.BeginBalance(mode))
            {
            }
        }

        foreach (var operation in LabelTable<DbOperation>.Texts)
        {
            LabelTable<DbOperation>.TryParse(operation, out var parsed).ShouldBeTrue();
            _telemetry.Db.CommandCompleted(parsed, TimeSpan.FromMilliseconds(2));
        }

        foreach (var reason in LabelTable<DbRetryReason>.Texts)
        {
            LabelTable<DbRetryReason>.TryParse(reason, out var parsed).ShouldBeTrue();
            _telemetry.Db.TransactionRetried(parsed);
        }

        foreach (var reason in LabelTable<AuthFailureReason>.Texts.Concat(HostileTexts))
        {
            _telemetry.Security.AuthFailed(reason);
        }

        foreach (var policy in LabelTable<RateLimitPolicy>.Texts.Concat(HostileTexts))
        {
            _telemetry.Security.RateLimitRejected(policy);
        }

        _telemetry.Security.AccountCreated();

        foreach (var purpose in LabelTable<PiiPurpose>.Texts.Concat(HostileTexts))
        {
            _telemetry.Security.PiiDecrypted(purpose, 3);
        }

        _telemetry.Security.KeyReloaded(true);
        _telemetry.Security.KeyReloaded(false);

        foreach (var eventType in LabelTable<AuditEventLabel>.Texts.Concat(HostileTexts))
        {
            foreach (var outcome in LabelTable<AuditOutcomeLabel>.Texts.Concat(HostileTexts))
            {
                _telemetry.Security.AuditRecorded(eventType, outcome);
            }
        }

        foreach (var reason in LabelTable<AuditSkipReason>.Texts.Concat(HostileTexts))
        {
            _telemetry.Security.AuditSkipped(reason);
        }

        _telemetry.Security.AccountsRewrapped(10, 2);

        _telemetry.OutboxPort.Published(5);
        _telemetry.OutboxPort.Pruned(5);

        foreach (var reason in Enum.GetValues<PublishFailureReason>())
        {
            _telemetry.OutboxPort.PublishFailed(reason);
        }

        _telemetry.OutboxPort.Measured(new OutboxStats(10, 3.5, 1));
        _telemetry.OutboxPort.CircuitStateChanged(BrokerCircuitState.Open);
        _telemetry.OutboxPort.BrokerConnected(true);
        _telemetry.Heartbeat.Set(WorkerLoop.Outbox, DateTimeOffset.UnixEpoch.AddDays(1));
        _telemetry.Heartbeat.Set(WorkerLoop.IntegrityRecent, DateTimeOffset.UnixEpoch.AddDays(1));
        _telemetry.Heartbeat.Set(WorkerLoop.IntegrityFull, DateTimeOffset.UnixEpoch.AddDays(1));

        using (var operation = _telemetry.OutboxPort.BeginPublish(EnvelopeOf()))
        {
            operation.Confirmed();
        }

        foreach (var mode in Enum.GetValues<IntegrityMode>())
        {
            foreach (var check in Enum.GetValues<IntegrityCheck>())
            {
                using var run = _telemetry.IntegrityPort.BeginRun(mode);
                run.Violation(check);
                run.Completed(false);
            }

            using (var run = _telemetry.IntegrityPort.BeginRun(mode))
            {
                run.Completed(true);
            }

            using (var run = _telemetry.IntegrityPort.BeginRun(mode))
            {
                run.Failed();
            }
        }

        foreach (var instrument in _telemetry.Capture.InstrumentNames)
        {
            _telemetry.Capture.Observe(instrument);
        }
    }

    private static OutboxEnvelope EnvelopeOf() =>
        new(
            Guid.Parse("0192b7c4-5d12-7c88-a0e4-9d3b6f1a2c75"),
            AccountId.From(Guid.Parse("0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33")).Value,
            "EntryRegistered",
            "{}",
            "9f3c1a7e2b4d4f60a1c8e5d7b3a29f10",
            null,
            DateTimeOffset.UnixEpoch,
            1);

    [GeneratedRegex(@"^(unknown|other|[A-Za-z0-9._:@-]{1,64})$", RegexOptions.CultureInvariant)]
    private static partial Regex ClientLabel();
}
