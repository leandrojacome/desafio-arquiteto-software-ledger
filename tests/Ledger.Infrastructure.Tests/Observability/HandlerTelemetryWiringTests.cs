using System.Diagnostics;
using Ledger.Application;
using Ledger.Application.Abstractions;
using Ledger.Application.Accounts;
using Ledger.Application.Balances;
using Ledger.Application.Entries;
using Ledger.Application.Integrity;
using Ledger.Application.Outbox;
using Ledger.Application.Security;
using Ledger.Application.Tests.Entries.Support;
using Ledger.Application.Tests.Integrity;
using Ledger.Application.Tests.Security;
using Ledger.Application.Tests.Support;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;
using Ledger.Infrastructure.Tests.Observability.Support;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Ledger.Infrastructure.Tests.Observability;

[Trait("Category", "Unit")]
public sealed class HandlerTelemetryWiringTests : IDisposable
{
    private static readonly DateTimeOffset Base = new(2026, 10, 1, 14, 0, 0, TimeSpan.Zero);

    private static readonly AccountId Account =
        AccountId.From(Guid.Parse("0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33")).Value;

    private readonly TestTelemetry _telemetry = new();
    private readonly CapturingLoggerFactory _loggers = new();

    public void Dispose()
    {
        _loggers.Dispose();
        _telemetry.Dispose();
    }

    private static OutboxSettings Settings() =>
        new(
            200,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(5),
            5,
            1000,
            1_000_000,
            TimeSpan.FromDays(7),
            5000);

    private static OutboxEnvelope Envelope(int number, string? traceParent = null) =>
        new(
            new Guid(number, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]),
            Account,
            "EntryRegistered",
            "{\"payload\":\"secret-value\"}",
            "corr-" + number,
            traceParent,
            Base.AddMinutes(number),
            1);

    [Fact]
    public async Task GetBalance_Current_ObservesTheDurationOfTheCurrentMode()
    {
        var reader = Substitute.For<IBalanceReader>();
        reader.ReadCurrentAsync(Account, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new CurrentBalanceReading("BRL", 850m, 0m, EntryFixtures.Entry, Base)));
        var handler = new GetBalanceHandler(
            reader,
            new BalanceReadSettings(TimeSpan.FromSeconds(5)),
            _telemetry.Reads,
            new ReadAudit(_loggers));
        using var request = TestActivities.Start("http.request");

        await handler.HandleAsync(
            new GetBalanceQuery(Account, null, EntryFixtures.ClientId, EntryFixtures.CorrelationId),
            CancellationToken.None);

        _telemetry.Capture.Count("ledger.balance.query.duration", ("mode", "current")).ShouldBe(1);
        _telemetry.Capture.SingleActivity("ledger.balance_query").GetTagItem("ledger.balance.mode").ShouldBe("current");
        LeakScanner.Find(LeakScanner.TextOf(_telemetry.Capture), ["850", EntryFixtures.ClientId]).ShouldBeEmpty();
    }

    [Fact]
    public async Task GetBalance_AsOf_ObservesTheDurationOfTheAsOfModeEvenWhenTheAccountIsMissing()
    {
        var reader = Substitute.For<IBalanceReader>();
        reader.ReadAtAsync(Account, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<BalanceAtReading>(AccountErrors.NotFound));
        var handler = new GetBalanceHandler(
            reader,
            new BalanceReadSettings(TimeSpan.FromSeconds(5)),
            _telemetry.Reads,
            new ReadAudit(_loggers));

        await handler.HandleAsync(
            new GetBalanceQuery(Account, Base, EntryFixtures.ClientId, EntryFixtures.CorrelationId),
            CancellationToken.None);

        _telemetry.Capture.Count("ledger.balance.query.duration", ("mode", "as_of")).ShouldBe(1);
    }

    [Fact]
    public async Task ListEntries_ReturnsAFullPage_RecordsTheCountAndTheNextFlagOnTheSpan()
    {
        var reader = Substitute.For<IStatementReader>();
        var protector = Substitute.For<IStatementCursorProtector>();
        protector.Protect(Arg.Any<AccountId>(), Arg.Any<StatementPosition>()).Returns("opaque-cursor-canary");
        reader.AccountExistsAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>()).Returns(true);
        reader.ReadPageAsync(
                Arg.Any<AccountId>(),
                Arg.Any<StatementBounds>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Enumerable.Range(1, 4).Select(index =>
                EntryFixtures.StoredView() with
                {
                    Id = EntryId.From(new Guid(index, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1])).Value,
                    AccountVersion = index,
                    RecordedAt = Base.AddMinutes(index)
                }).ToList());
        var handler = new ListEntriesHandler(reader, protector, _telemetry.Reads, new ReadAudit(_loggers));

        await handler.HandleAsync(
            new ListEntriesQuery(Account, null, null, 3, null, EntryFixtures.ClientId, EntryFixtures.CorrelationId),
            CancellationToken.None);

        var span = _telemetry.Capture.SingleActivity("ledger.statement_query");
        span.GetTagItem("ledger.statement.limit").ShouldBe(3);
        span.GetTagItem("ledger.statement.returned").ShouldBe(3);
        span.GetTagItem("ledger.statement.has_next").ShouldBe(true);
        LeakScanner.Find(LeakScanner.TextOf(_telemetry.Capture), ["opaque-cursor-canary", EntryFixtures.Description])
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task PublishBatch_WithConfirmedAndFailedMessages_EmitsTheOutboxSignals()
    {
        var queue = Substitute.For<IOutboxQueue>();
        var publisher = Substitute.For<IEventPublisher>();
        publisher.IsConnected.Returns(true);
        publisher.Circuit.Returns(BrokerCircuitState.Closed);
        publisher.ClaimBudget(Arg.Any<int>()).Returns(200);
        var traceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
        var ok = Envelope(1, traceParent);
        var failing = Envelope(2, traceParent);
        var other = Envelope(3);
        queue.ClaimBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns([ok, failing, other]);
        queue.MarkPublishedAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(2);
        publisher.PublishAsync(Arg.Is<OutboxEnvelope>(candidate => candidate.Id == failing.Id), Arg.Any<CancellationToken>())
            .ThrowsAsync(new EventPublishException(PublishFailureReason.Nack, failing.Id));
        var handler = new PublishOutboxBatchHandler(
            queue,
            publisher,
            _telemetry.OutboxPort,
            Settings(),
            TimeProvider.System,
            new CapturingLogger<PublishOutboxBatchHandler>());

        await handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);

        _telemetry.Capture.Sum("outbox.published").ShouldBe(2);
        _telemetry.Capture.Count("outbox.publish.failures", ("reason", "nack")).ShouldBe(1);
        _telemetry.Capture.Of("outbox.publish.duration").Count.ShouldBe(2);
        var poll = _telemetry.Capture.SingleActivity("outbox.poll");
        poll.GetTagItem("outbox.batch_size").ShouldBe(3);
        var publishes = _telemetry.Capture.Activities.Where(activity => activity.OperationName == "outbox.publish").ToList();
        publishes.Count.ShouldBe(3);
        publishes.ShouldAllBe(span => span.Parent == null && span.TraceId != poll.TraceId);
        publishes.Count(span => span.Links.Any()).ShouldBe(2);
        publishes.Count(span => span.GetTagItem("ledger.outcome") as string == "failed").ShouldBe(1);
        LeakScanner.Find(LeakScanner.TextOf(_telemetry.Capture), ["secret-value"]).ShouldBeEmpty();
    }

    [Fact]
    public async Task Prune_ReportsTheTotalOnce()
    {
        var queue = Substitute.For<IOutboxQueue>();
        queue.PruneAsync(Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(5000, 10);
        var handler = new PruneOutboxHandler(queue, _telemetry.OutboxPort, Settings(), new CapturingLogger<PruneOutboxHandler>());

        await handler.HandleAsync(CancellationToken.None);

        _telemetry.Capture.Of("outbox.pruned").ShouldHaveSingleItem().Value.ShouldBe(5010);
    }

    [Fact]
    public async Task Measure_FeedsTheObservableGaugesWithTheSnapshot()
    {
        var queue = Substitute.For<IOutboxQueue>();
        queue.ReadStatsAsync(Arg.Any<OutboxStatsRequest>(), Arg.Any<CancellationToken>())
            .Returns(new OutboxStats(42, null, 1));
        var handler = new MeasureOutboxHandler(queue, _telemetry.OutboxPort, Settings());

        await handler.HandleAsync(CancellationToken.None);

        _telemetry.Capture.Observe("outbox.pending.messages").ShouldHaveSingleItem().Value.ShouldBe(42);
        _telemetry.Capture.Observe("outbox.oldest_pending.age").ShouldHaveSingleItem().Value.ShouldBe(0);
        _telemetry.Capture.Observe("outbox.failed.messages").ShouldHaveSingleItem().Value.ShouldBe(1);
    }

    [Fact]
    public async Task IntegrityRun_WithAFinding_CountsTheRunTheViolationAndTheDuration()
    {
        await using var session = new FakeIntegritySession
        {
            RecentAccounts = [Account],
            HeadRows = _ => [new HeadRow(Account, 12345.67m, 0m, 1, EntryFixtures.Entry, 7.77m, 1, EntryFixtures.Entry)]
        };
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(Guid.Parse("5d1b7c0e-9a3f-4c28-b6e1-d04f7a92c3b8"));
        var handler = new RunIntegrityCheckHandler(
            new FakeIntegritySessions(session),
            _telemetry.IntegrityPort,
            Substitute.For<IWorkerHeartbeat>(),
            ids,
            _telemetry.Time,
            new CapturingLogger<RunIntegrityCheckHandler>());
        var command = new RunIntegrityCheckCommand(
            IntegrityMode.Recent,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(1),
            TimeSpan.FromHours(24),
            TimeSpan.FromMinutes(10),
            5000);

        await handler.HandleAsync(command, CancellationToken.None);

        _telemetry.Capture.Count("ledger.integrity.check.runs", ("mode", "incremental"), ("result", "violation"))
            .ShouldBe(1);
        _telemetry.Capture.Of("ledger.integrity.violations").ShouldNotBeEmpty();
        _telemetry.Capture.Of("ledger.integrity.violations").ShouldAllBe(
            measurement => measurement.Tag("kind") == "balance_mismatch");
        _telemetry.Capture.Of("ledger.integrity.check.duration").ShouldHaveSingleItem();
        _telemetry.Capture.SingleActivity("integrity.check").GetTagItem("integrity.accounts_checked").ShouldBe(1L);
        LeakScanner.Find(LeakScanner.TextOf(_telemetry.Capture), ["12345.67", "7.77"]).ShouldBeEmpty();
    }

    [Fact]
    public async Task IntegrityRun_ThatIsSkippedBecauseTheLockIsHeld_EmitsNothing()
    {
        var handler = new RunIntegrityCheckHandler(
            new FakeIntegritySessions(null),
            _telemetry.IntegrityPort,
            Substitute.For<IWorkerHeartbeat>(),
            Substitute.For<IIdGenerator>(),
            _telemetry.Time,
            new CapturingLogger<RunIntegrityCheckHandler>());
        var command = new RunIntegrityCheckCommand(
            IntegrityMode.Full,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(1),
            TimeSpan.FromHours(24),
            TimeSpan.FromMinutes(10),
            5000);

        await handler.HandleAsync(command, CancellationToken.None);

        _telemetry.Capture.All.ShouldBeEmpty();
        _telemetry.Capture.Activities.ShouldBeEmpty();
    }

    [Fact]
    public async Task Rewrap_CountsEachBatchByResultAndPurpose()
    {
        var rewrapper = Substitute.For<IAccountKeyRewrapper>();
        var protector = Substitute.For<IHolderDocumentProtector>();
        rewrapper.RewrapBatchAsync(
                Arg.Any<RewrapBatchRequest>(),
                Arg.Any<Func<RewrapRow, Result<ProtectedHolderDocument>>>(),
                Arg.Any<CancellationToken>())
            .Returns(
                new RewrapBatchResult(500, 498, 2, Account),
                new RewrapBatchResult(0, 0, 0, null));
        rewrapper.TryBeginPassAsync(Arg.Any<CancellationToken>()).Returns(Substitute.For<IAsyncDisposable>());
        rewrapper.ReadKeyUsageAsync(Arg.Any<CancellationToken>())
            .Returns(new KeyUsage(new Dictionary<int, long>()));
        var handler = new RewrapAccountsHandler(
            rewrapper,
            protector,
            new FakeKeyProvider(2, SecurityVectors.KeySetOne, SecurityVectors.KeySetTwo),
            _telemetry.Security,
            new CapturingLogger<RewrapAccountsHandler>());

        await handler.HandleAsync(new RewrapAccountsCommand(2, 500, "a1b2c3d4e5f60718293a4b5c6d7e8f90"), CancellationToken.None);

        _telemetry.Capture.Sum("ledger.pii.decrypt", ("purpose", "rewrap")).ShouldBe(498);
        _telemetry.Capture.Sum("ledger.rewrap.accounts", ("result", "rewrapped")).ShouldBe(498);
        _telemetry.Capture.Sum("ledger.rewrap.accounts", ("result", "failed")).ShouldBe(2);
        _telemetry.Capture.Activities.Count(activity => activity.OperationName == "pii.rewrap").ShouldBe(2);
    }
}
