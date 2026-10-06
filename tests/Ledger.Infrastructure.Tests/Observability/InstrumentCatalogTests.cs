using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;
using Ledger.Infrastructure.Tests.Observability.Support;

namespace Ledger.Infrastructure.Tests.Observability;

[Trait("Category", "Unit")]
public sealed partial class InstrumentCatalogTests : IDisposable
{
    private readonly TestTelemetry _telemetry = new();

    public static TheoryData<string, string?, string> Catalog() =>
        new()
        {
            { "ledger.entries.recorded", "{entry}", "counter" },
            { "ledger.entries.rejected", "{entry}", "counter" },
            { "ledger.entry.duration", "s", "histogram" },
            { "idempotency.replays", "{replay}", "counter" },
            { "idempotency.conflicts", "{conflict}", "counter" },
            { "ledger.recorded_at.corrections", "{correction}", "counter" },
            { "ledger.db.retries", "{retry}", "counter" },
            { "ledger.db.command.duration", "s", "histogram" },
            { "ledger.balance.query.duration", "s", "histogram" },
            { "ledger.auth.failures", "{failure}", "counter" },
            { "ledger.rate_limit.rejections", "{rejection}", "counter" },
            { "ledger.accounts.created", "{account}", "counter" },
            { "ledger.pii.decrypt", "{account}", "counter" },
            { "ledger.key.reloads", "{reload}", "counter" },
            { "ledger.audit.recorded", "{event}", "counter" },
            { "ledger.audit.skipped", "{event}", "counter" },
            { "ledger.rewrap.accounts", "{account}", "counter" },
            { "outbox.pending.messages", "{message}", "gauge" },
            { "outbox.oldest_pending.age", "s", "gauge" },
            { "outbox.published", "{message}", "counter" },
            { "outbox.publish.failures", "{failure}", "counter" },
            { "outbox.publish.duration", "s", "histogram" },
            { "outbox.failed.messages", "{message}", "gauge" },
            { "outbox.pruned", "{message}", "counter" },
            { "broker.circuit_breaker.state", null, "gauge" },
            { "broker.connected", null, "gauge" },
            { "worker.last_cycle.timestamp", "s", "gauge" },
            { "ledger.worker.loop.failures", "{failure}", "counter" },
            { "ledger.worker.loop.last_success.timestamp", "s", "gauge" },
            { "ledger.pii.accounts.below_active_key", "{account}", "gauge" },
            { "ledger.integrity.check.runs", "{run}", "counter" },
            { "ledger.integrity.violations", "{violation}", "counter" },
            { "ledger.integrity.last.success.timestamp", "s", "gauge" },
            { "ledger.integrity.check.duration", "s", "histogram" }
        };

    public void Dispose()
    {
        _telemetry.Dispose();
    }

    [Theory]
    [MemberData(nameof(Catalog))]
    public void Instrument_ExistsWithTheDeclaredUnitAndKindOnTheLedgerMeter(string name, string? unit, string kind)
    {
        _telemetry.Capture.InstrumentNames.ShouldContain(name);
        var instrument = _telemetry.Capture.Instrument(name);

        instrument.Meter.Name.ShouldBe("Ledger");
        instrument.Unit.ShouldBe(unit);
        KindOf(instrument).ShouldBe(kind);
    }

    [Fact]
    public void Meter_HoldsExactlyTheInstrumentsOfTheCatalog()
    {
        var expected = Catalog().Select(row => (string)row[0]).Order().ToList();

        _telemetry.Capture.InstrumentNames.Order().ShouldBe(expected);
    }

    [Fact]
    public void Instruments_ExposeAHumanReadableDescription()
    {
        foreach (var name in _telemetry.Capture.InstrumentNames)
        {
            _telemetry.Capture.Instrument(name).Description.ShouldNotBeNullOrWhiteSpace(name);
        }
    }

    [Fact]
    public void EveryMetricOfTheDocumentation_HasAnInstrumentUnderItsPrometheusName()
    {
        var instruments = PrometheusNames();

        var missing = DocumentedMetrics().Where(name => !instruments.Contains(name)).ToList();

        missing.ShouldBeEmpty($"Documented without an instrument: {string.Join(", ", missing)}");
    }

    [Fact]
    public void EveryInstrument_IsDocumentedUnderItsPrometheusName()
    {
        var documented = DocumentedMetrics().ToHashSet();

        var undocumented = PrometheusNames().Where(name => !documented.Contains(name)).ToList();

        undocumented.ShouldBeEmpty($"Instruments missing from the metrics catalog page: {string.Join(", ", undocumented)}");
    }

    [Fact]
    public void PrometheusConversion_FollowsTheRulesOfTheContract()
    {
        PrometheusNames().ShouldContain("ledger_entries_recorded_total");
        PrometheusNames().ShouldContain("ledger_entry_duration_seconds");
        PrometheusNames().ShouldContain("idempotency_replays_total");
        PrometheusNames().ShouldContain("outbox_pending_messages");
        PrometheusNames().ShouldContain("outbox_oldest_pending_age_seconds");
        PrometheusNames().ShouldContain("broker_circuit_breaker_state");
        PrometheusNames().ShouldContain("worker_last_cycle_timestamp_seconds");
        PrometheusNames().ShouldContain("ledger_integrity_last_success_timestamp_seconds");
        PrometheusNames().ShouldContain("ledger_integrity_violations_total");
    }

    private static string KindOf(Instrument instrument)
    {
        var type = instrument.GetType().Name;

        return type.StartsWith("Counter", StringComparison.Ordinal) ? "counter"
            : type.StartsWith("Histogram", StringComparison.Ordinal) ? "histogram"
            : type.StartsWith("ObservableGauge", StringComparison.Ordinal) ? "gauge"
            : type;
    }

    private HashSet<string> PrometheusNames()
    {
        return _telemetry.Capture.InstrumentNames
            .Select(name => PrometheusName(_telemetry.Capture.Instrument(name)))
            .ToHashSet();
    }

    private static string PrometheusName(Instrument instrument)
    {
        var name = instrument.Name.Replace('.', '_');

        if (instrument.Unit == "s" && !name.EndsWith("_seconds", StringComparison.Ordinal))
        {
            name += "_seconds";
        }

        if (KindOf(instrument) == "counter")
        {
            name += "_total";
        }

        return name;
    }

    private static List<string> DocumentedMetrics()
    {
        var text = File.ReadAllText(RepositoryRoot.File("docs", "08-resiliencia-e-operacao", "catalogo-de-metricas.md"));

        return MetricRow().Matches(text)
            .Select(match => match.Groups["name"].Value)
            .Where(name => !name.StartsWith("http_", StringComparison.Ordinal))
            .Distinct()
            .ToList();
    }

    [GeneratedRegex(@"^\| `(?<name>[a-z][a-z0-9_]*)` \|", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex MetricRow();
}
