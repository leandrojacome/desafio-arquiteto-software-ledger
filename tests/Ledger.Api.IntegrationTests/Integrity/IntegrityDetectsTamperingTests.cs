using System.Text.Json;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application.Integrity;
using Ledger.Domain.Accounts;

namespace Ledger.Api.IntegrationTests.Integrity;

[Collection(MessagingCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class IntegrityDetectsTamperingTests(PostgresFixture postgres)
{
    [DockerFact]
    public async Task AHealthyLedger_ProducesNoViolationAndASuccessfulRunRecord()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);
        var account = await harness.Ledger.SeedAsync();

        var summary = await harness.RunAsync(IntegrityMode.Recent);
        var audit = await harness.Ledger.AuditRowsAsync("integrity.");

        summary.Skipped.ShouldBeFalse();
        summary.Violations.ShouldBe(0);
        summary.AccountsChecked.ShouldBe(1);
        summary.EntriesChecked.ShouldBe(account.Entries.Count);
        audit.ShouldHaveSingleItem().Outcome.ShouldBe("SUCCESS");
        harness.Telemetry.Runs.ShouldHaveSingleItem().Clean.ShouldBe(true);
    }

    [DockerFact]
    public async Task ATamperedStoredBalance_IsReportedAsHeadBalanceAndFailsTheDirectSum()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);
        var account = await harness.Ledger.SeedAsync();

        await harness.Ledger.TamperStoredBalanceAsync(account.AccountId, 1.00m);

        var summary = await harness.RunAsync(IntegrityMode.Recent);
        var violation = await SingleViolationAsync(harness);

        summary.Violations.ShouldBe(1);
        Check(violation, "HEAD_BALANCE", account.AccountId, expected: "110.00", found: "111.00");
        violation.EntryId.ShouldBeNull();

        await using var session = await harness.Sessions.OpenAsync(CancellationToken.None);

        var report = await session.InspectAccountAsync(
            AccountId.From(account.AccountId).Value,
            CancellationToken.None);

        report.Findings.Select(finding => finding.Check).ShouldContain(IntegrityCheck.SumBalance);
        report.StoredBalance.ShouldBe(111.00m);
        report.EntriesSum.ShouldBe(110.00m);
        report.EntryCount.ShouldBe(5);
    }

    [DockerFact]
    public async Task ATamperedBalanceAfter_ProducesTwoChainDriftsAndKeepsTheDirectSumClean()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);
        var account = await harness.Ledger.SeedAsync();
        var third = account.Entries[2];
        var fourth = account.Entries[3];

        await harness.Ledger.TamperBalanceAfterAsync(third.Id, 1.00m);

        var summary = await harness.RunAsync(IntegrityMode.Recent);
        var violations = await ViolationsAsync(harness);

        summary.Violations.ShouldBe(2);
        violations.ShouldAllBe(violation => violation.Check == "CHAIN_DRIFT");

        var onThird = violations.Single(violation => violation.EntryId == third.Id);
        var onFourth = violations.Single(violation => violation.EntryId == fourth.Id);

        onThird.Expected.ShouldBe("120.00");
        onThird.Found.ShouldBe("121.00");
        onThird.AccountVersion.ShouldBe(3);
        onFourth.Expected.ShouldBe("101.00");
        onFourth.Found.ShouldBe("100.00");
        onFourth.AccountVersion.ShouldBe(4);

        await using var session = await harness.Sessions.OpenAsync(CancellationToken.None);

        var report = await session.InspectAccountAsync(
            AccountId.From(account.AccountId).Value,
            CancellationToken.None);

        report.Findings.Select(finding => finding.Check).ShouldNotContain(IntegrityCheck.SumBalance);
    }

    [DockerFact]
    public async Task ATamperedVersion_IsReportedAsHeadVersion()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);
        var account = await harness.Ledger.SeedAsync();

        await harness.Ledger.TamperVersionAsync(account.AccountId, 1);

        var summary = await harness.RunAsync(IntegrityMode.Recent);
        var violation = await SingleViolationAsync(harness);

        summary.Violations.ShouldBe(1);
        Check(violation, "HEAD_VERSION", account.AccountId, expected: "5", found: "6");
    }

    [DockerFact]
    public async Task ATamperedLastEntryPointer_IsReportedAsHeadLastEntry()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);
        var account = await harness.Ledger.SeedAsync();
        var wrong = account.Entries[1];

        await harness.Ledger.TamperLastEntryAsync(account.AccountId, wrong.Id);

        var summary = await harness.RunAsync(IntegrityMode.Recent);
        var violation = await SingleViolationAsync(harness);

        summary.Violations.ShouldBe(1);
        Check(
            violation,
            "HEAD_LAST_ENTRY",
            account.AccountId,
            expected: account.Entries[^1].Id.ToString("D"),
            found: wrong.Id.ToString("D"));
    }

    [DockerFact]
    public async Task ARemovedMiddleEntry_IsReportedAsOneChainGapOnTheFollowingEntry()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);
        var account = await harness.Ledger.SeedAsync();
        var fourth = account.Entries[3];

        await harness.Ledger.DeleteEntryAsync(account.Entries[2].Id);

        var summary = await harness.RunAsync(IntegrityMode.Recent);
        var violation = await SingleViolationAsync(harness);

        summary.Violations.ShouldBe(1);
        Check(violation, "CHAIN_GAP", account.AccountId, expected: "3", found: "missing");
        violation.EntryId.ShouldBe(fourth.Id);
        violation.AccountVersion.ShouldBe(4);
    }

    [DockerFact]
    public async Task ARegressingRecordedAt_IsReportedAsChainNonMonotonic()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);
        var account = await harness.Ledger.SeedAsync();
        var third = account.Entries[2];
        var fourth = account.Entries[3];

        await harness.Ledger.TamperRecordedAtAsync(fourth.Id, third.RecordedAt - TimeSpan.FromSeconds(1));

        var summary = await harness.RunAsync(IntegrityMode.Recent);
        var violation = await SingleViolationAsync(harness);

        summary.Violations.ShouldBe(1);
        violation.Check.ShouldBe("CHAIN_NON_MONOTONIC");
        violation.EntryId.ShouldBe(fourth.Id);
        violation.AccountVersion.ShouldBe(4);
        violation.Expected.ShouldStartWith("after ");
    }

    [DockerFact]
    public async Task ABalanceBelowTheFloor_IsReportedAsHeadFloorOnly()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);
        var account = await harness.Ledger.SeedOverdrawnAsync();

        var summary = await harness.RunAsync(IntegrityMode.Recent);
        var violation = await SingleViolationAsync(harness);

        summary.Violations.ShouldBe(1);
        Check(violation, "HEAD_FLOOR", account.AccountId, expected: "0.00", found: "-30.00");
    }

    [DockerFact]
    public async Task TheFullRun_ChecksEveryAccountEvenWithoutRecentEntries()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);
        var old = await harness.Ledger.SeedAsync(await harness.Ledger.DatabaseNowAsync() - TimeSpan.FromHours(30));
        var fresh = await harness.Ledger.SeedAsync();

        await harness.Ledger.TamperStoredBalanceAsync(old.AccountId, 5.00m);

        var recent = await harness.RunAsync(IntegrityMode.Recent);
        var full = await harness.RunAsync(IntegrityMode.Full);

        recent.Violations.ShouldBe(0);
        recent.AccountsChecked.ShouldBe(1);
        full.AccountsChecked.ShouldBe(2);
        full.Violations.ShouldBe(1);
        (await ViolationsAsync(harness)).ShouldHaveSingleItem().AccountId.ShouldBe(old.AccountId);
        fresh.AccountId.ShouldNotBe(old.AccountId);
    }

    [DockerFact]
    public async Task ARunWithViolations_IsRecordedAsFailureAndReportedToTelemetry()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);
        var account = await harness.Ledger.SeedAsync();

        await harness.Ledger.TamperStoredBalanceAsync(account.AccountId, 2.00m);
        await harness.RunAsync(IntegrityMode.Recent);

        var runRow = (await harness.Ledger.AuditRowsAsync("integrity.run_completed")).ShouldHaveSingleItem();
        var run = harness.Telemetry.Runs.ShouldHaveSingleItem();

        runRow.Outcome.ShouldBe("FAILURE");
        run.Clean.ShouldBe(false);
        run.Violations.ShouldBe([IntegrityCheck.HeadBalance]);
    }

    private static void Check(ViolationRow violation, string check, Guid accountId, string expected, string found)
    {
        violation.Check.ShouldBe(check);
        violation.AccountId.ShouldBe(accountId);
        violation.Expected.ShouldBe(expected);
        violation.Found.ShouldBe(found);
    }

    private static async Task<ViolationRow> SingleViolationAsync(IntegrityHarness harness) =>
        (await ViolationsAsync(harness)).ShouldHaveSingleItem();

    private static async Task<IReadOnlyList<ViolationRow>> ViolationsAsync(IntegrityHarness harness)
    {
        var rows = await harness.Ledger.AuditRowsAsync("integrity.violation_detected");

        return [.. rows.Select(Parse)];
    }

    private static ViolationRow Parse(AuditRow row)
    {
        using var document = JsonDocument.Parse(row.Details);

        var root = document.RootElement;

        return new ViolationRow(
            row.AccountId ?? Guid.Empty,
            root.GetProperty("check").GetString() ?? string.Empty,
            root.TryGetProperty("entryId", out var entry) ? Guid.Parse(entry.GetString() ?? string.Empty) : null,
            root.TryGetProperty("accountVersion", out var version) ? version.GetInt64() : null,
            root.GetProperty("expected").GetString() ?? string.Empty,
            root.GetProperty("found").GetString() ?? string.Empty);
    }

    private sealed record ViolationRow(
        Guid AccountId,
        string Check,
        Guid? EntryId,
        long? AccountVersion,
        string Expected,
        string Found);
}
