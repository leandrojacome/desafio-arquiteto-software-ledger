using Ledger.Application.Abstractions;
using Ledger.Application.Outbox;
using Ledger.Domain.Accounts;
using Ledger.Domain.Shared;
using Microsoft.Extensions.Logging;

namespace Ledger.Application.Integrity;

public sealed class RunIntegrityCheckHandler(
    IIntegritySessions sessions,
    IIntegrityTelemetry telemetry,
    IWorkerHeartbeat heartbeat,
    IIdGenerator idGenerator,
    TimeProvider timeProvider,
    ILogger<RunIntegrityCheckHandler> logger)
{
    public async Task<Result<IntegrityRunSummary>> HandleAsync(
        RunIntegrityCheckCommand command,
        CancellationToken cancellationToken)
    {
        var session = await sessions.TryBeginRunAsync(command.Mode, cancellationToken);

        if (session is null)
        {
            return IntegrityRunSummary.SkippedRun(command.Mode);
        }

        await using (session)
        {
            return await RunCheckAsync(session, command, cancellationToken);
        }
    }

    private static IntegrityWindow ResolveFullWindow(RunIntegrityCheckCommand command, DateTimeOffset now) =>
        new(now - command.FullLookback, now);

    private static IEnumerable<IntegrityWindow> Slice(IntegrityWindow window, TimeSpan slice)
    {
        for (var start = window.Start; start < window.End; start += slice)
        {
            var end = start + slice;

            yield return new IntegrityWindow(start, end < window.End ? end : window.End);
        }
    }

    private static WorkerLoop LoopFor(IntegrityMode mode) =>
        mode == IntegrityMode.Recent ? WorkerLoop.IntegrityRecent : WorkerLoop.IntegrityFull;

    private async Task<Result<IntegrityRunSummary>> RunCheckAsync(
        IIntegritySession session,
        RunIntegrityCheckCommand command,
        CancellationToken cancellationToken)
    {
        using var run = telemetry.BeginRun(command.Mode);
        var runId = idGenerator.NewId().ToString("N");
        var startedAt = timeProvider.GetTimestamp();

        try
        {
            var now = await session.GetDatabaseNowAsync(cancellationToken);
            var window = await ResolveWindowAsync(session, command, now, cancellationToken);
            var tally = new RunTally(session, run, runId, command.Mode, LoopFor(command.Mode));

            if (command.Mode == IntegrityMode.Full)
            {
                await CheckAllHeadsAsync(tally, command, cancellationToken);
                await CheckChainAsync(tally, command, window, cancellationToken);
            }
            else
            {
                await CheckRecentAsync(tally, command, window, cancellationToken);
            }

            var summary = tally.Summarize(window.Start, window.End, partial: false);

            await session.RecordRunAsync(runId, summary, cancellationToken);

            run.AccountsChecked(summary.AccountsChecked);
            run.Completed(summary.Violations == 0);

            LogCompleted(command.Mode, runId, summary, startedAt);

            return summary;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            run.Failed();
            throw;
        }
    }

    private async Task<IntegrityWindow> ResolveWindowAsync(
        IIntegritySession session,
        RunIntegrityCheckCommand command,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (command.Mode == IntegrityMode.Full)
        {
            return ResolveFullWindow(command, now);
        }

        var last = await session.FindLastRunAsync(command.Mode, cancellationToken);
        var start = last is null
            ? now - (command.RecentInterval + command.Overlap)
            : last.WindowEnd - command.Overlap;
        var floor = now - command.FullLookback;

        if (start < floor)
        {
            IntegrityLog.RecentWindowTruncated(logger, now - start, command.FullLookback);
            start = floor;
        }

        return new IntegrityWindow(start > now ? now : start, now);
    }

    private async Task CheckRecentAsync(
        RunTally tally,
        RunIntegrityCheckCommand command,
        IntegrityWindow window,
        CancellationToken cancellationToken)
    {
        var slices = Slice(window, command.ChainSlice).ToList();
        var seen = new HashSet<AccountId>();

        for (var index = 0; index < slices.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var slice = slices[index];
            var accounts = await tally.Session.ReadRecentAccountsAsync(slice.Start, slice.End, cancellationToken);
            var unseen = accounts.Where(seen.Add).ToList();

            foreach (var batch in unseen.Chunk(command.HeadBatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();

                await CheckHeadBatchAsync(tally, batch, cancellationToken);
            }

            await CheckChainSliceAsync(tally, slice, cancellationToken);

            if (index < slices.Count - 1)
            {
                var checkpoint = tally.Summarize(window.Start, slice.End, partial: true);

                await tally.Session.RecordRunAsync(tally.RunId, checkpoint, cancellationToken);
            }
        }
    }

    private async Task CheckAllHeadsAsync(
        RunTally tally,
        RunIntegrityCheckCommand command,
        CancellationToken cancellationToken)
    {
        AccountId? after = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = await tally.Session.ReadAccountBatchAsync(after, command.HeadBatchSize, cancellationToken);

            if (batch.Count == 0)
            {
                return;
            }

            await CheckHeadBatchAsync(tally, batch, cancellationToken);

            if (batch.Count < command.HeadBatchSize)
            {
                return;
            }

            after = batch[^1];
        }
    }

    private async Task CheckHeadBatchAsync(
        RunTally tally,
        IReadOnlyList<AccountId> accounts,
        CancellationToken cancellationToken)
    {
        var rows = await tally.Session.CheckHeadsAsync(accounts, cancellationToken);

        foreach (var row in rows)
        {
            await ReportAsync(tally, IntegrityClassifier.FromHead(row), cancellationToken);
        }

        tally.CountAccounts(accounts.Count);
        heartbeat.Beat(tally.Loop);
    }

    private async Task CheckChainAsync(
        RunTally tally,
        RunIntegrityCheckCommand command,
        IntegrityWindow window,
        CancellationToken cancellationToken)
    {
        foreach (var slice in Slice(window, command.ChainSlice))
        {
            cancellationToken.ThrowIfCancellationRequested();

            await CheckChainSliceAsync(tally, slice, cancellationToken);
        }
    }

    private async Task CheckChainSliceAsync(
        RunTally tally,
        IntegrityWindow slice,
        CancellationToken cancellationToken)
    {
        var rows = await tally.Session.CheckChainAsync(slice.Start, slice.End, cancellationToken);

        foreach (var row in rows)
        {
            await ReportAsync(tally, IntegrityClassifier.FromChain(row), cancellationToken);
        }

        tally.CountEntries(await tally.Session.CountEntriesAsync(slice.Start, slice.End, cancellationToken));
        heartbeat.Beat(tally.Loop);
    }

    private async Task ReportAsync(
        RunTally tally,
        IReadOnlyList<IntegrityFinding> findings,
        CancellationToken cancellationToken)
    {
        foreach (var finding in findings)
        {
            await tally.Session.RecordViolationAsync(tally.RunId, tally.Mode, finding, cancellationToken);

            tally.Run.Violation(finding.Check);
            tally.CountViolation();

            LogViolation(tally, finding);
        }
    }

    private void LogCompleted(IntegrityMode mode, string runId, IntegrityRunSummary summary, long startedAt)
    {
        if (!logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        var modeText = mode.AuditText();
        var duration = timeProvider.GetElapsedTime(startedAt);

        IntegrityLog.IntegrityRunCompleted(
            logger,
            modeText,
            runId,
            summary.AccountsChecked,
            summary.EntriesChecked,
            summary.Violations,
            duration);
    }

    private void LogViolation(RunTally tally, IntegrityFinding finding)
    {
        if (!logger.IsEnabled(LogLevel.Error))
        {
            return;
        }

        var modeText = tally.Mode.AuditText();
        var checkName = finding.Check.AuditName();

        IntegrityLog.IntegrityViolationDetected(
            logger,
            modeText,
            tally.RunId,
            checkName,
            finding.AccountId,
            finding.EntryId,
            finding.AccountVersion);
    }

    private readonly record struct IntegrityWindow(DateTimeOffset Start, DateTimeOffset End);

    private sealed class RunTally(
        IIntegritySession session,
        IIntegrityRunTelemetry run,
        string runId,
        IntegrityMode mode,
        WorkerLoop loop)
    {
        public IIntegritySession Session { get; } = session;

        public IIntegrityRunTelemetry Run { get; } = run;

        public string RunId { get; } = runId;

        public IntegrityMode Mode { get; } = mode;

        public WorkerLoop Loop { get; } = loop;

        public long AccountsChecked { get; private set; }

        public long EntriesChecked { get; private set; }

        public int Violations { get; private set; }

        public void CountAccounts(int count) => AccountsChecked += count;

        public void CountEntries(long count) => EntriesChecked += count;

        public void CountViolation() => Violations++;

        public IntegrityRunSummary Summarize(DateTimeOffset windowStart, DateTimeOffset windowEnd, bool partial) =>
            new(Mode, false, windowStart, windowEnd, AccountsChecked, EntriesChecked, Violations, partial);
    }
}
