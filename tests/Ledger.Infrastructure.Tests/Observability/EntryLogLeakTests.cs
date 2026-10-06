using System.Diagnostics;
using Ledger.Application.Abstractions;
using Ledger.Application.Entries;
using Ledger.Application.Tests.Entries.Support;
using Ledger.Infrastructure.Observability;
using Ledger.Infrastructure.Tests.Observability.Support;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace Ledger.Infrastructure.Tests.Observability;

[Trait("Category", "Unit")]
public sealed class EntryLogLeakTests : IDisposable
{
    private const string AmountText = "80.00";
    private const string BalanceText = "920.00";

    private readonly TestTelemetry _telemetry = new();
    private readonly CollectingLogSink _sink = new();
    private readonly Logger _logger;
    private readonly SerilogLoggerFactory _factory;
    private readonly Activity _request;

    public EntryLogLeakTests()
    {
        _logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WithLedgerIdentity("ledger-api", "Testing")
            .WithLedgerEnrichers()
            .WithLedgerProtection()
            .WriteTo.Sink(_sink)
            .CreateLogger();
        _factory = new SerilogLoggerFactory(_logger);
        _request = TestActivities.Start("http.request");
        _request.SetTag(ActivityTags.ClientId, EntryFixtures.ClientId);
    }

    private static string[] Canaries =>
    [
        EntryFixtures.Description,
        EntryFixtures.Reference,
        EntryFixtures.KeyText,
        "Cobrança em duplicidade confirmada pela conciliação",
        AmountText,
        BalanceText
    ];

    public void Dispose()
    {
        _request.Dispose();
        _factory.Dispose();
        _logger.Dispose();
        _telemetry.Dispose();
    }

    private async Task RunTheTrafficAsync()
    {
        var accepted = new WriteHarness();
        var register = new RegisterEntryHandler(
            accepted.UnitOfWork,
            accepted.Ids,
            _telemetry.Entries,
            LoggerFor<RegisterEntryHandler>());
        var command = EntryFixtures.RegisterCommand();

        await register.HandleAsync(command, CancellationToken.None);

        var replayed = new WriteHarness();
        replayed.ReserveReturns(false);
        replayed.FindReturns(StoredRecord(command));
        await RegisterWith(replayed).HandleAsync(command, CancellationToken.None);

        var conflicted = new WriteHarness();
        conflicted.ReserveReturns(false);
        conflicted.FindReturns(StoredRecord(EntryFixtures.RegisterCommand(amount: 10m)));
        await RegisterWith(conflicted).HandleAsync(command, CancellationToken.None);

        var refused = new WriteHarness();
        refused.ApplyReturns(ApplyErrors.NotMatched);
        refused.DiagnosisReturns(EntryFixtures.AccountSnapshot(10m));
        await RegisterWith(refused).HandleAsync(command, CancellationToken.None);

        var corrected = new WriteHarness();
        corrected.ApplyReturns(
            Ledger.Domain.Shared.Result.Success(new AppliedEntry(EntryFixtures.StoredView(), true)));
        await RegisterWith(corrected).HandleAsync(command, CancellationToken.None);

        var reversed = new WriteHarness();
        reversed.OriginalReturns(EntryFixtures.Candidate());
        var reverse = new ReverseEntryHandler(
            reversed.UnitOfWork,
            reversed.Ids,
            _telemetry.Entries,
            LoggerFor<ReverseEntryHandler>());
        await reverse.HandleAsync(EntryFixtures.ReverseCommand(), CancellationToken.None);
    }

    private ILogger<T> LoggerFor<T>() => LoggerFactoryExtensions.CreateLogger<T>(_factory);

    private RegisterEntryHandler RegisterWith(WriteHarness harness) =>
        new(harness.UnitOfWork, harness.Ids, _telemetry.Entries, LoggerFor<RegisterEntryHandler>());

    private static IdempotencyRecord StoredRecord(RegisterEntryCommand command) =>
        new(CanonicalRequestHash.ForRegistration(command), CanonicalRequestHash.CurrentVersion, EntryFixtures.StoredView());

    [Fact]
    public async Task Traffic_NeverLeaksTheDescriptionTheReferenceTheKeyOrTheMoneyIntoLogs()
    {
        await RunTheTrafficAsync();

        var informationOrAbove = _sink.Events.Where(logEvent => logEvent.Level >= LogEventLevel.Information).ToList();

        informationOrAbove.ShouldNotBeEmpty();
        LeakScanner.Find(string.Join('\n', informationOrAbove.Select(_sink.Json)), Canaries).ShouldBeEmpty();
    }

    [Fact]
    public async Task Traffic_NeverLeaksTheFreeTextOrTheKeyEvenAtDebug()
    {
        await RunTheTrafficAsync();

        LeakScanner.Find(
                _sink.AllJson(),
                [EntryFixtures.Description, EntryFixtures.Reference, EntryFixtures.KeyText])
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task Traffic_NeverLeaksIntoMetricLabelsOrSpanAttributes()
    {
        await RunTheTrafficAsync();

        var emitted = LeakScanner.TextOf(_telemetry.Capture);

        emitted.ShouldNotBeNullOrWhiteSpace();
        LeakScanner.Find(emitted, Canaries).ShouldBeEmpty();
    }

    [Fact]
    public async Task Traffic_EmitsTheKeyFingerprintOfEightCharactersAndTheDocumentedLevels()
    {
        await RunTheTrafficAsync();

        var replay = Single(1002);
        var conflict = Single(1003);
        var insufficient = Single(1004);

        replay.Level.ShouldBe(LogEventLevel.Information);
        conflict.Level.ShouldBe(LogEventLevel.Warning);
        insufficient.Level.ShouldBe(LogEventLevel.Information);

        foreach (var logEvent in new[] { replay, conflict, insufficient })
        {
            CollectingLogSink.Property(logEvent, "KeyFingerprint").Trim('"').Length.ShouldBe(8);
            CollectingLogSink.Property(logEvent, "KeyFingerprint").Trim('"').ShouldBe(EntryFixtures.KeyFingerprint);
        }
    }

    [Fact]
    public async Task Traffic_StampsTheEventsOfTheRequestWithTheClientAndTheTrace()
    {
        await RunTheTrafficAsync();

        var accepted = _sink.Events.Where(logEvent => EventId(logEvent) == 1001).ToList();

        accepted.ShouldNotBeEmpty();
        accepted.ShouldAllBe(logEvent => CollectingLogSink.Property(logEvent, "ClientId") == $"\"{EntryFixtures.ClientId}\"");
        accepted.ShouldAllBe(logEvent => CollectingLogSink.Property(logEvent, "TraceId").Length > 2);
        accepted.ShouldAllBe(logEvent => CollectingLogSink.Property(logEvent, "Service") == "\"ledger-api\"");
    }

    private LogEvent Single(int eventId) => _sink.Events.Single(logEvent => EventId(logEvent) == eventId);

    private static int EventId(LogEvent logEvent) =>
        logEvent.Properties.TryGetValue("EventId", out var value) && value is StructureValue structure
            ? (int)(structure.Properties.Single(property => property.Name == "Id").Value as ScalarValue)!.Value!
            : 0;
}
