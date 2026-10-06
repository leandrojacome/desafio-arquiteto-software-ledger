using Ledger.Application.Abstractions;
using Ledger.Application.Integrity;
using Ledger.Application.Outbox;
using Ledger.Application.Tests.Support;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Ledger.Application.Tests.Integrity;

[Trait("Category", "Unit")]
public sealed class RunIntegrityCheckHandlerTests
{
    private const string RunId = "5d1b7c0e9a3f4c28b6e1d04f7a92c3b8";

    private static readonly Guid RunGuid = Guid.Parse("5d1b7c0e-9a3f-4c28-b6e1-d04f7a92c3b8");
    private static readonly EntryId Latest = EntryId.From(Guid.Parse("0192b7c4-5d11-7a3e-9c2f-3b1e7d9a4f10")).Value;
    private static readonly EntryId Other = EntryId.From(Guid.Parse("0192b7c4-5d11-7a3e-9c2f-3b1e7d9a4f11")).Value;

    private FakeIntegritySession Session { get; } = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 1, 22, 5, 0, TimeSpan.Zero));
    private readonly IIntegrityTelemetry _telemetry = Substitute.For<IIntegrityTelemetry>();
    private readonly IIntegrityRunTelemetry _run = Substitute.For<IIntegrityRunTelemetry>();
    private readonly IWorkerHeartbeat _heartbeat = Substitute.For<IWorkerHeartbeat>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly CapturingLogger<RunIntegrityCheckHandler> _logger = new();

    public RunIntegrityCheckHandlerTests()
    {
        _telemetry.BeginRun(Arg.Any<IntegrityMode>()).Returns(_run);
        _ids.NewId().Returns(RunGuid);
    }

    private static RunIntegrityCheckCommand Command(
        IntegrityMode mode = IntegrityMode.Recent,
        int headBatchSize = 5000,
        TimeSpan? chainSlice = null,
        TimeSpan? recentInterval = null,
        TimeSpan? overlap = null,
        TimeSpan? fullLookback = null) =>
        new(
            mode,
            recentInterval ?? TimeSpan.FromMinutes(5),
            overlap ?? TimeSpan.FromMinutes(1),
            fullLookback ?? TimeSpan.FromHours(24),
            chainSlice ?? TimeSpan.FromMinutes(10),
            headBatchSize);

    private static AccountId Account(int number) =>
        AccountId.From(new Guid(number, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1])).Value;

    private static List<AccountId> Accounts(int count) => Enumerable.Range(1, count).Select(Account).ToList();

    private RunIntegrityCheckHandler Handler() => Handler(new FakeIntegritySessions(Session));

    [Fact]
    public async Task HandleAsync_LockHeldByAnotherInstance_ReturnsSkippedAndCallsNothingElse()
    {
        var sessions = new FakeIntegritySessions(null);

        var result = await Handler(sessions).HandleAsync(Command(), CancellationToken.None);

        result.Value.Skipped.ShouldBeTrue();
        result.Value.Mode.ShouldBe(IntegrityMode.Recent);
        sessions.Attempts.ShouldBe([IntegrityMode.Recent]);
        _telemetry.DidNotReceive().BeginRun(Arg.Any<IntegrityMode>());
        _heartbeat.DidNotReceive().Beat(Arg.Any<WorkerLoop>());
        _ids.DidNotReceive().NewId();
    }

    [Fact]
    public async Task HandleAsync_RecentWithoutAPreviousRun_StartsAtNowMinusIntervalPlusOverlap()
    {
        Session.LastRun = null;

        var result = await Handler().HandleAsync(Command(), CancellationToken.None);

        result.Value.WindowStart.ShouldBe(Session.Now - TimeSpan.FromMinutes(6));
        result.Value.WindowEnd.ShouldBe(Session.Now);
        Session.LastRunLookups.ShouldBe([IntegrityMode.Recent]);
    }

    [Fact]
    public async Task HandleAsync_RecentWithAPreviousRun_StartsAtItsWindowEndMinusTheOverlap()
    {
        var previousEnd = Session.Now - TimeSpan.FromMinutes(5);
        Session.LastRun = new IntegrityRunRecord(
            previousEnd,
            "SUCCESS",
            IntegrityMode.Recent,
            previousEnd - TimeSpan.FromMinutes(5),
            previousEnd);

        var result = await Handler().HandleAsync(Command(), CancellationToken.None);

        result.Value.WindowStart.ShouldBe(previousEnd - TimeSpan.FromMinutes(1));
        result.Value.WindowEnd.ShouldBe(Session.Now);
    }

    [Fact]
    public async Task HandleAsync_PreviousWindowEndAfterNow_BringsTheStartToTheEnd()
    {
        var previousEnd = Session.Now + TimeSpan.FromMinutes(10);
        Session.LastRun = new IntegrityRunRecord(previousEnd, "SUCCESS", IntegrityMode.Recent, Session.Now, previousEnd);

        var result = await Handler().HandleAsync(Command(), CancellationToken.None);

        result.Value.WindowStart.ShouldBe(Session.Now);
        Session.ChainSlices.ShouldBeEmpty();
    }

    [Fact]
    public async Task HandleAsync_FullRun_UsesTheLookbackAndDoesNotReadThePreviousRun()
    {
        var result = await Handler().HandleAsync(Command(IntegrityMode.Full), CancellationToken.None);

        result.Value.WindowStart.ShouldBe(Session.Now - TimeSpan.FromHours(24));
        result.Value.WindowEnd.ShouldBe(Session.Now);
        Session.LastRunLookups.ShouldBeEmpty();
    }

    [Fact]
    public async Task HandleAsync_RecentRunWithTwelveThousandAccounts_ChecksHeadsInBatchesOfFiveThousand()
    {
        Session.RecentAccounts = Accounts(12_000);

        var result = await Handler().HandleAsync(Command(), CancellationToken.None);

        Session.HeadBatches.Select(batch => batch.Count).ShouldBe([5_000, 5_000, 2_000]);
        result.Value.AccountsChecked.ShouldBe(12_000);
    }

    [Fact]
    public async Task HandleAsync_WindowOfTwentyFiveMinutes_IsSlicedInContiguousTenTenAndFive()
    {
        var windowEnd = Session.Now - TimeSpan.FromMinutes(24);
        Session.LastRun = new IntegrityRunRecord(windowEnd, "SUCCESS", IntegrityMode.Recent, windowEnd - TimeSpan.FromMinutes(5), windowEnd);

        var result = await Handler().HandleAsync(Command(), CancellationToken.None);

        var start = result.Value.WindowStart;
        start.ShouldBe(Session.Now - TimeSpan.FromMinutes(25));
        Session.ChainSlices.ShouldBe(
        [
            (start, start + TimeSpan.FromMinutes(10)),
            (start + TimeSpan.FromMinutes(10), start + TimeSpan.FromMinutes(20)),
            (start + TimeSpan.FromMinutes(20), Session.Now)
        ]);
    }

    [Fact]
    public async Task HandleAsync_RecentRunOfSeveralSlices_ReadsTheAccountsSliceBySliceNeverInOneUnboundedQuery()
    {
        var windowEnd = Session.Now - TimeSpan.FromMinutes(24);
        Session.LastRun = new IntegrityRunRecord(windowEnd, "SUCCESS", IntegrityMode.Recent, windowEnd, windowEnd);

        await Handler().HandleAsync(Command(), CancellationToken.None);

        Session.RecentReads.Count.ShouldBe(3);
        Session.RecentReads.ShouldBe(Session.ChainSlices);
        Session.RecentReads.ShouldAllBe(read => read.End - read.Start <= TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task HandleAsync_AccountThatAppearsInEverySlice_HasItsHeadCheckedOnlyOnce()
    {
        var windowEnd = Session.Now - TimeSpan.FromMinutes(24);
        Session.LastRun = new IntegrityRunRecord(windowEnd, "SUCCESS", IntegrityMode.Recent, windowEnd, windowEnd);
        Session.RecentAccountsFor = (_, _) => [Account(1), Account(2)];

        var result = await Handler().HandleAsync(Command(), CancellationToken.None);

        Session.HeadBatches.Single().ShouldBe([Account(1), Account(2)]);
        result.Value.AccountsChecked.ShouldBe(2);
    }

    [Fact]
    public async Task HandleAsync_RecentRunOfSeveralSlices_RecordsACheckpointAfterEachFinishedSliceExceptTheLast()
    {
        var windowEnd = Session.Now - TimeSpan.FromMinutes(24);
        Session.LastRun = new IntegrityRunRecord(windowEnd, "SUCCESS", IntegrityMode.Recent, windowEnd, windowEnd);
        Session.RecentAccountsFor = (start, _) => start == Session.Now - TimeSpan.FromMinutes(25) ? Accounts(3) : [];

        var result = await Handler().HandleAsync(Command(), CancellationToken.None);

        var start = result.Value.WindowStart;
        Session.RunRecords.Count.ShouldBe(3);
        Session.RunRecords.Select(record => record.Summary.Partial).ShouldBe([true, true, false]);
        Session.RunRecords.Select(record => record.Summary.WindowEnd).ShouldBe(
        [
            start + TimeSpan.FromMinutes(10),
            start + TimeSpan.FromMinutes(20),
            Session.Now
        ]);
        Session.RunRecords.ShouldAllBe(record => record.Summary.WindowStart == start && record.RunId == RunId);
        Session.RunRecords[0].Summary.AccountsChecked.ShouldBe(3);
        Session.RunRecords[0].Summary.EntriesChecked.ShouldBe(10);
        Session.RunRecords[1].Summary.EntriesChecked.ShouldBe(20);
        Session.RunRecords[2].Summary.EntriesChecked.ShouldBe(30);
    }

    [Fact]
    public async Task HandleAsync_RecentRunOfASingleSlice_RecordsOnlyTheFinalRun()
    {
        await Handler().HandleAsync(Command(), CancellationToken.None);

        Session.RunRecords.Single().Summary.Partial.ShouldBeFalse();
    }

    [Fact]
    public async Task HandleAsync_RunThatFailsInTheMiddle_KeepsTheCheckpointsOfTheSlicesThatFinished()
    {
        var windowEnd = Session.Now - TimeSpan.FromMinutes(24);
        Session.LastRun = new IntegrityRunRecord(windowEnd, "SUCCESS", IntegrityMode.Recent, windowEnd, windowEnd);
        var calls = 0;
        Session.OnCheckChain = () =>
        {
            if (++calls == 3)
            {
                throw new TimeoutException("db");
            }
        };

        await Should.ThrowAsync<TimeoutException>(() => Handler().HandleAsync(Command(), CancellationToken.None));

        var start = Session.Now - TimeSpan.FromMinutes(25);
        Session.RunRecords.Select(record => (record.Summary.Partial, record.Summary.WindowEnd)).ShouldBe(
        [
            (true, start + TimeSpan.FromMinutes(10)),
            (true, start + TimeSpan.FromMinutes(20))
        ]);
    }

    [Fact]
    public async Task HandleAsync_NextRunAfterAFailure_ContinuesFromTheLastCheckpoint()
    {
        var windowEnd = Session.Now - TimeSpan.FromHours(10);
        Session.LastRun = new IntegrityRunRecord(windowEnd, "SUCCESS", IntegrityMode.Recent, windowEnd, windowEnd);
        var calls = 0;
        Session.OnCheckChain = () =>
        {
            if (++calls == 7)
            {
                throw new TimeoutException("db");
            }
        };

        await Should.ThrowAsync<TimeoutException>(() => Handler().HandleAsync(Command(), CancellationToken.None));

        var checkpoint = Session.RunRecords.Last().Summary;
        Session.LastRun = new IntegrityRunRecord(Session.Now, "SUCCESS", IntegrityMode.Recent, checkpoint.WindowStart, checkpoint.WindowEnd);
        Session.OnCheckChain = null;
        Session.ChainSlices.Clear();

        var next = await Handler().HandleAsync(Command(), CancellationToken.None);

        checkpoint.Partial.ShouldBeTrue();
        next.Value.WindowStart.ShouldBe(checkpoint.WindowEnd - TimeSpan.FromMinutes(1));
        (Session.Now - next.Value.WindowStart).ShouldBeLessThan(TimeSpan.FromHours(10));
    }

    [Fact]
    public async Task HandleAsync_RecentWindowOlderThanTheFullLookback_IsCutToTheLookbackAndWarns()
    {
        var windowEnd = Session.Now - TimeSpan.FromHours(30);
        Session.LastRun = new IntegrityRunRecord(windowEnd, "SUCCESS", IntegrityMode.Recent, windowEnd, windowEnd);

        var result = await Handler().HandleAsync(Command(), CancellationToken.None);

        result.Value.WindowStart.ShouldBe(Session.Now - TimeSpan.FromHours(24));
        Session.RecentReads.Count.ShouldBe(144);
        var log = _logger.Single(4005);
        log.Level.ShouldBe(LogLevel.Warning);
        log.Properties["Lookback"].ShouldBe("1.00:00:00");
    }

    [Fact]
    public async Task HandleAsync_RecentWindowInsideTheFullLookback_DoesNotWarn()
    {
        var windowEnd = Session.Now - TimeSpan.FromHours(23);
        Session.LastRun = new IntegrityRunRecord(windowEnd, "SUCCESS", IntegrityMode.Recent, windowEnd, windowEnd);

        await Handler().HandleAsync(Command(), CancellationToken.None);

        _logger.Contains(4005).ShouldBeFalse();
    }

    [Fact]
    public async Task HandleAsync_FullRunOfTwentyFourHours_IsSlicedInOneHundredAndFortyFourParts()
    {
        var result = await Handler().HandleAsync(Command(IntegrityMode.Full), CancellationToken.None);

        Session.ChainSlices.Count.ShouldBe(144);
        Session.ChainSlices[0].Start.ShouldBe(result.Value.WindowStart);
        Session.ChainSlices[^1].End.ShouldBe(result.Value.WindowEnd);

        for (var index = 1; index < Session.ChainSlices.Count; index++)
        {
            Session.ChainSlices[index].Start.ShouldBe(Session.ChainSlices[index - 1].End);
        }
    }

    [Fact]
    public async Task HandleAsync_FullRun_WalksTheAccountBatchesAdvancingTheKeyUntilASmallerBatch()
    {
        var accounts = Accounts(250);
        Session.AllAccounts = accounts;

        var result = await Handler().HandleAsync(Command(IntegrityMode.Full, headBatchSize: 100), CancellationToken.None);

        Session.BatchReads.ShouldBe([(null, 100), (accounts[99], 100), (accounts[199], 100)]);
        Session.HeadBatches.Select(batch => batch.Count).ShouldBe([100, 100, 50]);
        result.Value.AccountsChecked.ShouldBe(250);
    }

    [Fact]
    public async Task HandleAsync_FullRunWithAFullLastBatch_ReadsOneMoreEmptyBatchAndStops()
    {
        Session.AllAccounts = Accounts(200);

        await Handler().HandleAsync(Command(IntegrityMode.Full, headBatchSize: 100), CancellationToken.None);

        Session.BatchReads.Count.ShouldBe(3);
        Session.HeadBatches.Count.ShouldBe(2);
    }

    [Fact]
    public async Task HandleAsync_EntriesCounted_SumsTheCountOfEverySlice()
    {
        var windowEnd = Session.Now - TimeSpan.FromMinutes(24);
        Session.LastRun = new IntegrityRunRecord(windowEnd, "SUCCESS", IntegrityMode.Recent, windowEnd, windowEnd);
        Session.EntryCount = (_, _) => 7;

        var result = await Handler().HandleAsync(Command(), CancellationToken.None);

        result.Value.EntriesChecked.ShouldBe(21);
    }

    [Fact]
    public async Task HandleAsync_CleanRun_RecordsTheRunWithZeroViolationsAndReportsCompleted()
    {
        Session.RecentAccounts = Accounts(3);

        var result = await Handler().HandleAsync(Command(), CancellationToken.None);

        var summary = result.Value;
        summary.Violations.ShouldBe(0);
        summary.Skipped.ShouldBeFalse();
        Session.RunRecords.ShouldBe([(RunId, summary)]);
        Session.Violations.ShouldBeEmpty();
        _run.Received(1).Completed(true);
        _run.Received(1).AccountsChecked(3);
        _run.DidNotReceive().Failed();
        _run.DidNotReceive().Violation(Arg.Any<IntegrityCheck>());
    }

    [Fact]
    public async Task HandleAsync_CleanRun_LogsTheCompletionAtInformation()
    {
        Session.RecentAccounts = Accounts(3);
        Session.ChainRows = (_, _) => [];
        Session.OnCheckChain = () => _time.Advance(TimeSpan.FromSeconds(3));

        await Handler().HandleAsync(Command(), CancellationToken.None);

        var log = _logger.Single(4001);
        log.Level.ShouldBe(LogLevel.Information);
        log.Properties["Mode"].ShouldBe("RECENT");
        log.Properties["RunId"].ShouldBe(RunId);
        log.Properties["AccountsChecked"].ShouldBe("3");
        log.Properties["Violations"].ShouldBe("0");
        log.Properties["Duration"].ShouldBe("00:00:03");
    }

    [Fact]
    public async Task HandleAsync_FindingsInHeadsAndChain_RecordEachOneAndCountThem()
    {
        var account = Account(1);
        Session.RecentAccounts = [account];
        Session.HeadRows = _ => [new HeadRow(account, -10m, 5m, 7, Other, 3m, 3, Latest)];
        var windowEnd = Session.Now - TimeSpan.FromMinutes(4);
        Session.LastRun = new IntegrityRunRecord(windowEnd, "SUCCESS", IntegrityMode.Recent, windowEnd, windowEnd);
        Session.ChainRows = (_, _) =>
            [new ChainRow(account, 9, Latest, 921m, Session.Now, Session.Now.AddSeconds(-1), 1m, false, false)];

        var result = await Handler().HandleAsync(Command(), CancellationToken.None);

        result.Value.Violations.ShouldBe(5);
        Session.Violations.Select(violation => violation.Finding.Check).ShouldBe(
        [
            IntegrityCheck.HeadBalance,
            IntegrityCheck.HeadVersion,
            IntegrityCheck.HeadLastEntry,
            IntegrityCheck.HeadFloor,
            IntegrityCheck.ChainDrift
        ]);
        Session.Violations.ShouldAllBe(violation => violation.RunId == RunId && violation.Mode == IntegrityMode.Recent);
        Session.RunRecords.Single().Summary.Violations.ShouldBe(5);
        _run.Received(5).Violation(Arg.Any<IntegrityCheck>());
        _run.Received(1).Violation(IntegrityCheck.ChainDrift);
        _run.Received(1).Completed(false);
    }

    [Fact]
    public async Task HandleAsync_Finding_LogsAtErrorWithoutAnyMoneyValue()
    {
        var account = Account(1);
        Session.RecentAccounts = [account];
        Session.HeadRows = _ => [new HeadRow(account, 12345.67m, 0m, 1, Latest, 7.77m, 1, Latest)];

        await Handler().HandleAsync(Command(), CancellationToken.None);

        var log = _logger.Single(4002);
        log.Level.ShouldBe(LogLevel.Error);
        log.Properties["Mode"].ShouldBe("RECENT");
        log.Properties["RunId"].ShouldBe(RunId);
        log.Properties["Check"].ShouldBe("HEAD_BALANCE");
        log.Properties["AccountId"].ShouldBe(account.ToString());
        foreach (var entry in _logger.Entries)
        {
            entry.Message.ShouldNotContain("12345.67");
            entry.Message.ShouldNotContain("7.77");
            entry.Properties.Values.ShouldNotContain("12345.67");
            entry.Properties.Values.ShouldNotContain("7.77");
        }
    }

    [Fact]
    public async Task HandleAsync_ChainFinding_LogsTheEntryAndTheVersion()
    {
        var account = Account(1);
        var windowEnd = Session.Now - TimeSpan.FromMinutes(4);
        Session.LastRun = new IntegrityRunRecord(windowEnd, "SUCCESS", IntegrityMode.Recent, windowEnd, windowEnd);
        Session.ChainRows = (_, _) =>
            [new ChainRow(account, 9, Latest, 921m, Session.Now, Session.Now.AddSeconds(-1), 0m, true, false)];

        await Handler().HandleAsync(Command(), CancellationToken.None);

        var log = _logger.Single(4002);
        log.Properties["Check"].ShouldBe("CHAIN_GAP");
        log.Properties["EntryId"].ShouldBe(Latest.ToString());
        log.Properties["AccountVersion"].ShouldBe("9");
    }

    [Fact]
    public async Task HandleAsync_SessionThrows_ReportsFailureDoesNotRecordTheRunAndRethrows()
    {
        Session.OnCheckChain = () => throw new TimeoutException("db");

        await Should.ThrowAsync<TimeoutException>(() => Handler().HandleAsync(Command(), CancellationToken.None));

        _run.Received(1).Failed();
        _run.DidNotReceive().Completed(Arg.Any<bool>());
        Session.RunRecords.ShouldBeEmpty();
        Session.Disposed.ShouldBeTrue();
        _run.Received(1).Dispose();
    }

    [Fact]
    public async Task HandleAsync_Success_AlwaysDisposesTheSession()
    {
        await Handler().HandleAsync(Command(), CancellationToken.None);

        Session.Disposed.ShouldBeTrue();
        _run.Received(1).Dispose();
    }

    [Fact]
    public async Task HandleAsync_CancellationBetweenBatches_StopsWithoutRecordingTheRunNorReportingFailure()
    {
        using var source = new CancellationTokenSource();
        Session.RecentAccounts = Accounts(12_000);
        Session.OnCheckHeads = () => source.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(
            () => Handler().HandleAsync(Command(), source.Token));

        Session.HeadBatches.Count.ShouldBe(1);
        Session.RunRecords.ShouldBeEmpty();
        _run.DidNotReceive().Failed();
        Session.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task HandleAsync_BeatsTheIntegrityHeartbeatAfterEveryHeadBatchAndEveryChainSlice()
    {
        Session.RecentAccounts = Accounts(12_000);
        var windowEnd = Session.Now - TimeSpan.FromMinutes(24);
        Session.LastRun = new IntegrityRunRecord(windowEnd, "SUCCESS", IntegrityMode.Recent, windowEnd, windowEnd);

        await Handler().HandleAsync(Command(), CancellationToken.None);

        _heartbeat.Received(6).Beat(WorkerLoop.IntegrityRecent);
        _heartbeat.DidNotReceive().Beat(WorkerLoop.IntegrityFull);
        _heartbeat.DidNotReceive().Beat(WorkerLoop.Outbox);
    }

    [Fact]
    public async Task HandleAsync_FullRun_BeatsAfterEveryAccountBatchAndSlice()
    {
        Session.AllAccounts = Accounts(250);

        await Handler().HandleAsync(Command(IntegrityMode.Full, headBatchSize: 100), CancellationToken.None);

        _heartbeat.Received(3 + 144).Beat(WorkerLoop.IntegrityFull);
        _heartbeat.DidNotReceive().Beat(WorkerLoop.IntegrityRecent);
    }

    [Fact]
    public async Task HandleAsync_GeneratesTheRunIdInThirtyTwoHexadecimalCharacters()
    {
        Session.RecentAccounts = [Account(1)];
        Session.HeadRows = _ => [new HeadRow(Account(1), 1m, 0m, 1, Latest, 2m, 1, Latest)];

        await Handler().HandleAsync(Command(), CancellationToken.None);

        Session.Violations.Single().RunId.Length.ShouldBe(32);
        Session.Violations.Single().RunId.ShouldNotContain("-");
        Session.RunRecords.Single().RunId.ShouldBe(RunId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    public void Command_WithANonPositiveChainSlice_Throws(int seconds)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Command(chainSlice: TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData(IntegrityMode.Recent, 0)]
    [InlineData(IntegrityMode.Full, 0)]
    [InlineData(IntegrityMode.Recent, -1)]
    [InlineData(IntegrityMode.Full, -1)]
    public void Command_WithAHeadBatchSizeBelowOne_Throws(IntegrityMode mode, int headBatchSize)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Command(mode, headBatchSize));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Command_WithANonPositiveRecentIntervalOrFullLookback_Throws(int minutes)
    {
        var value = TimeSpan.FromMinutes(minutes);

        Should.Throw<ArgumentOutOfRangeException>(() => Command(recentInterval: value));
        Should.Throw<ArgumentOutOfRangeException>(() => Command(fullLookback: value));
    }

    [Fact]
    public void Command_WithANegativeOverlap_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Command(overlap: TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void Command_WithZeroOverlapAndTheSmallestBatch_IsAccepted()
    {
        var command = Command(headBatchSize: 1, overlap: TimeSpan.Zero);

        command.HeadBatchSize.ShouldBe(1);
        command.Overlap.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public async Task HandleAsync_TokenIsPassedToTheSession()
    {
        var sessions = Substitute.For<IIntegritySessions>();
        sessions.TryBeginRunAsync(Arg.Any<IntegrityMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IIntegritySession?>(null));
        using var source = new CancellationTokenSource();

        await Handler(sessions).HandleAsync(Command(), source.Token);

        await sessions.Received(1).TryBeginRunAsync(IntegrityMode.Recent, source.Token);
    }

    private RunIntegrityCheckHandler Handler(IIntegritySessions sessions) =>
        new(sessions, _telemetry, _heartbeat, _ids, _time, _logger);
}
