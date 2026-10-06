using System.Globalization;
using System.Text.Json;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application.Integrity;
using Ledger.Application.Outbox;

namespace Ledger.Api.IntegrationTests.Integrity;

[Collection(MessagingCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class IntegrityWindowTests(PostgresFixture postgres)
{
    private static readonly TimeSpan Overlap = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    [DockerFact]
    public async Task WithoutAPreviousRun_TheRecentWindowCoversTheIntervalPlusTheOverlap()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);

        var summary = await harness.RunAsync(IntegrityMode.Recent);

        (summary.WindowEnd - summary.WindowStart).ShouldBe(Interval + Overlap);
    }

    [DockerFact]
    public async Task TheSecondRecentRun_StartsWhereTheFirstEndedMinusTheOverlap_ReadFromTheAuditTrail()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);

        var first = await harness.RunAsync(IntegrityMode.Recent);
        var second = await harness.RunAsync(IntegrityMode.Recent);
        var rows = await harness.Ledger.AuditRowsAsync("integrity.run_completed");

        second.WindowStart.ShouldBe(first.WindowEnd - Overlap);
        rows.Count.ShouldBe(2);
        WindowEndOf(rows[0]).ShouldBe(first.WindowEnd);
        WindowEndOf(rows[1]).ShouldBe(second.WindowEnd);
    }

    [DockerFact]
    public async Task ATransactionThatCommitsLateWithAnInstantInsideThePreviousWindow_IsCaughtByTheOverlap()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);
        var account = await harness.Ledger.SeedAsync();

        var first = await harness.RunAsync(IntegrityMode.Recent);

        first.Violations.ShouldBe(0);

        var late = account.Entries[^1] with
        {
            Id = Guid.CreateVersion7(),
            Version = account.Entries[^1].Version + 1,
            Type = "CREDIT",
            Amount = 5.00m,
            BalanceAfter = 999.00m,
            RecordedAt = first.WindowEnd - TimeSpan.FromSeconds(20)
        };

        await harness.Ledger.InsertEntryDirectlyAsync(account.AccountId, late);

        var second = await harness.RunAsync(IntegrityMode.Recent);

        second.Violations.ShouldBeGreaterThan(0);
        second.WindowStart.ShouldBe(first.WindowEnd - Overlap);
        late.RecordedAt.ShouldBeGreaterThanOrEqualTo(second.WindowStart);
        late.RecordedAt.ShouldBeLessThan(first.WindowEnd);
    }

    [DockerFact]
    public async Task ARecentRunAfterALongStoppage_WalksTheWindowInSlicesAndLeavesACheckpointPerFinishedSlice()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);
        var now = await harness.Ledger.DatabaseNowAsync();
        var stoppedAt = now - TimeSpan.FromHours(3);

        await harness.Ledger.SeedAsync(now - TimeSpan.FromHours(2));
        await harness.Ledger.InsertRunCompletedAsync(IntegrityMode.Recent, stoppedAt - Interval, stoppedAt);

        var summary = await harness.RunAsync(IntegrityMode.Recent);
        var rows = (await harness.Ledger.AuditRowsAsync("integrity.run_completed"))
            .Where(row => row.CorrelationId != "seeded-run")
            .ToList();

        var slices = (int)Math.Ceiling((summary.WindowEnd - summary.WindowStart) / TimeSpan.FromMinutes(10));

        slices.ShouldBeGreaterThan(15);
        rows.Count.ShouldBe(slices);
        rows.Take(slices - 1).ShouldAllBe(row => PartialOf(row));
        PartialOf(rows[^1]).ShouldBeFalse();
        WindowEndOf(rows[^1]).ShouldBe(summary.WindowEnd);
        rows.Select(WindowEndOf).ShouldBeInOrder();
        summary.Violations.ShouldBe(0);
        summary.EntriesChecked.ShouldBe(5);
        summary.AccountsChecked.ShouldBe(1);
    }

    [DockerFact]
    public async Task ARecentRunStoppedForMoreThanTheFullLookback_IsCutToTheLookbackAndLeftToTheFullRun()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);
        var now = await harness.Ledger.DatabaseNowAsync();
        var stoppedAt = now - TimeSpan.FromHours(40);

        await harness.Ledger.InsertRunCompletedAsync(IntegrityMode.Recent, stoppedAt - Interval, stoppedAt);

        var summary = await harness.RunAsync(IntegrityMode.Recent);

        (summary.WindowEnd - summary.WindowStart).ShouldBe(TimeSpan.FromHours(24));
        harness.HandlerLog.Events.ShouldContain(captured => captured.Id == 4005);
    }

    [DockerFact]
    public async Task AFullRun_AlwaysCoversTheLastTwentyFourHours()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);

        var summary = await harness.RunAsync(IntegrityMode.Full);

        (summary.WindowEnd - summary.WindowStart).ShouldBe(TimeSpan.FromHours(24));
    }

    [DockerFact]
    public async Task TheHeartbeatOfTheIntegrityLoop_AdvancesWhileARunRuns()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);
        var before = harness.Heartbeat.LastBeat(WorkerLoop.IntegrityRecent) ?? throw new InvalidOperationException();

        await harness.Ledger.SeedAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(20));
        await harness.RunAsync(IntegrityMode.Recent);

        (harness.Heartbeat.LastBeat(WorkerLoop.IntegrityRecent) ?? before).ShouldBeGreaterThan(before);
    }

    private static bool PartialOf(AuditRow row)
    {
        using var document = JsonDocument.Parse(row.Details);

        return document.RootElement.TryGetProperty("partial", out var partial) && partial.GetBoolean();
    }

    private static DateTimeOffset WindowEndOf(AuditRow row)
    {
        using var document = JsonDocument.Parse(row.Details);

        return DateTimeOffset.ParseExact(
            document.RootElement.GetProperty("windowEnd").GetString() ?? string.Empty,
            "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }
}
