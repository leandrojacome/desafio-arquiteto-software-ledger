using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application.Integrity;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Infrastructure.Persistence.Integrity;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Integrity;

[Collection(MessagingCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresIntegritySessionTests(PostgresFixture postgres)
{
    [DockerFact]
    public async Task HealthyAccounts_ProduceNoRowsInTheHeadAndChainChecks()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);
        var account = await harness.Ledger.SeedAsync();
        var now = await harness.Ledger.DatabaseNowAsync();

        await using var session = await harness.Sessions.OpenAsync(CancellationToken.None);

        var accountId = AccountId.From(account.AccountId).Value;

        (await session.CheckHeadsAsync([accountId], CancellationToken.None)).ShouldBeEmpty();
        (await session.CheckChainAsync(now - TimeSpan.FromHours(1), now, CancellationToken.None)).ShouldBeEmpty();
    }

    [DockerFact]
    public async Task InspectAccount_OfAHealthyAccount_HasNoFindingsAndTheRightSumAndCount()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);
        var account = await harness.Ledger.SeedAsync();

        await using var session = await harness.Sessions.OpenAsync(CancellationToken.None);

        var report = await session.InspectAccountAsync(AccountId.From(account.AccountId).Value, CancellationToken.None);

        report.Findings.ShouldBeEmpty();
        report.StoredBalance.ShouldBe(110.00m);
        report.EntriesSum.ShouldBe(110.00m);
        report.EntryCount.ShouldBe(5);
    }

    [DockerFact]
    public async Task InspectAccount_OfAnUnknownAccount_ReportsNothing()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);

        await using var session = await harness.Sessions.OpenAsync(CancellationToken.None);

        var report = await session.InspectAccountAsync(AccountId.From(Guid.NewGuid()).Value, CancellationToken.None);

        report.Findings.ShouldBeEmpty();
        report.EntryCount.ShouldBe(0);
    }

    [DockerFact]
    public async Task ReadRecentAccounts_ReturnsExactlyTheAccountsWithEntriesInTheHalfOpenWindow()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);
        var now = await harness.Ledger.DatabaseNowAsync();
        var inside = await harness.Ledger.SeedAsync(now - TimeSpan.FromMinutes(4));
        var outside = await harness.Ledger.SeedAsync(now - TimeSpan.FromHours(3));

        await using var session = await harness.Sessions.OpenAsync(CancellationToken.None);

        var found = await session.ReadRecentAccountsAsync(now - TimeSpan.FromMinutes(5), now, CancellationToken.None);
        var firstEntry = inside.Entries[0].RecordedAt;
        var excludedEnd = await session.ReadRecentAccountsAsync(
            now - TimeSpan.FromMinutes(5),
            firstEntry,
            CancellationToken.None);
        var includedStart = await session.ReadRecentAccountsAsync(
            firstEntry,
            firstEntry + TimeSpan.FromMicroseconds(1),
            CancellationToken.None);

        found.Select(id => id.Value).ShouldBe([inside.AccountId]);
        excludedEnd.ShouldBeEmpty();
        includedStart.Select(id => id.Value).ShouldBe([inside.AccountId]);
        outside.AccountId.ShouldNotBe(inside.AccountId);
    }

    [DockerFact]
    public async Task ReadAccountBatch_ReturnsAccountsInKeyOrderRespectingAfterAndTheSize()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);
        var seeded = new List<Guid>();

        for (var index = 0; index < 5; index++)
        {
            seeded.Add((await harness.Ledger.SeedAsync()).AccountId);
        }

        seeded.Sort();

        await using var session = await harness.Sessions.OpenAsync(CancellationToken.None);

        var firstPage = await session.ReadAccountBatchAsync(null, 2, CancellationToken.None);
        var secondPage = await session.ReadAccountBatchAsync(firstPage[^1], 2, CancellationToken.None);
        var lastPage = await session.ReadAccountBatchAsync(secondPage[^1], 2, CancellationToken.None);
        var beyond = await session.ReadAccountBatchAsync(lastPage[^1], 2, CancellationToken.None);

        firstPage.Select(id => id.Value).ShouldBe(seeded.Take(2));
        secondPage.Select(id => id.Value).ShouldBe(seeded.Skip(2).Take(2));
        lastPage.Select(id => id.Value).ShouldBe(seeded.Skip(4));
        beyond.ShouldBeEmpty();
    }

    [DockerFact]
    public async Task CountEntries_CountsTheEntriesOfTheWindow()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);
        var now = await harness.Ledger.DatabaseNowAsync();

        await harness.Ledger.SeedAsync(now - TimeSpan.FromMinutes(4));
        await harness.Ledger.SeedAsync(now - TimeSpan.FromMinutes(4));
        await harness.Ledger.SeedAsync(now - TimeSpan.FromHours(5));

        await using var session = await harness.Sessions.OpenAsync(CancellationToken.None);

        (await session.CountEntriesAsync(now - TimeSpan.FromMinutes(10), now, CancellationToken.None)).ShouldBe(10);
        (await session.CountEntriesAsync(now - TimeSpan.FromHours(10), now, CancellationToken.None)).ShouldBe(15);
    }

    [DockerFact]
    public async Task GetDatabaseNow_IsCloseToTheClockOfTheDatabase()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);

        await using var session = await harness.Sessions.OpenAsync(CancellationToken.None);

        var reported = await session.GetDatabaseNowAsync(CancellationToken.None);
        var actual = await harness.Ledger.DatabaseNowAsync();

        (actual - reported).ShouldBeLessThan(TimeSpan.FromSeconds(5));
        (actual - reported).ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    [DockerFact]
    public async Task FindLastRun_IsNullUntilARunIsRecorded_AndThenReturnsTheLatestOfThatMode()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);
        var now = await harness.Ledger.DatabaseNowAsync();

        await using var session = await harness.Sessions.OpenAsync(CancellationToken.None);

        (await session.FindLastRunAsync(IntegrityMode.Recent, CancellationToken.None)).ShouldBeNull();

        await session.RecordRunAsync(
            "run-1",
            new IntegrityRunSummary(IntegrityMode.Recent, false, now - TimeSpan.FromMinutes(5), now, 10, 20, 0),
            CancellationToken.None);
        await session.RecordRunAsync(
            "run-2",
            new IntegrityRunSummary(IntegrityMode.Recent, false, now, now + TimeSpan.FromMinutes(5), 11, 21, 2),
            CancellationToken.None);

        var last = await session.FindLastRunAsync(IntegrityMode.Recent, CancellationToken.None);

        last.ShouldNotBeNull();
        last.Mode.ShouldBe(IntegrityMode.Recent);
        last.Outcome.ShouldBe("FAILURE");
        last.WindowStart.ShouldBe(now);
        last.WindowEnd.ShouldBe(now + TimeSpan.FromMinutes(5));
        (await session.FindLastRunAsync(IntegrityMode.Full, CancellationToken.None)).ShouldBeNull();
    }

    [DockerFact]
    public async Task RecordRunAndRecordViolation_WriteTheRowsOfTheContractThroughTheWorkerRole()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);
        var accountId = AccountId.From(Guid.NewGuid()).Value;
        var entryId = EntryId.From(Guid.NewGuid()).Value;
        var now = await harness.Ledger.DatabaseNowAsync();

        await using var session = await harness.Sessions.OpenAsync(CancellationToken.None);

        await session.RecordViolationAsync(
            "0123456789abcdef0123456789abcdef",
            IntegrityMode.Full,
            new IntegrityFinding(IntegrityCheck.ChainDrift, accountId, entryId, 7, "10.00", "11.00"),
            CancellationToken.None);
        await session.RecordRunAsync(
            "0123456789abcdef0123456789abcdef",
            new IntegrityRunSummary(IntegrityMode.Full, false, now - TimeSpan.FromHours(24), now, 3, 9, 1),
            CancellationToken.None);

        var rows = await harness.Ledger.AuditRowsAsync("integrity.");
        var violation = rows.Single(row => row.EventType == "integrity.violation_detected");
        var run = rows.Single(row => row.EventType == "integrity.run_completed");

        violation.ClientId.ShouldBe("ledger-worker");
        violation.CorrelationId.ShouldBe("0123456789abcdef0123456789abcdef");
        violation.AccountId.ShouldBe(accountId.Value);
        violation.Outcome.ShouldBe("FAILURE");
        run.Outcome.ShouldBe("FAILURE");

        using var details = JsonDocument.Parse(violation.Details);

        details.RootElement.GetProperty("check").GetString().ShouldBe("CHAIN_DRIFT");
        details.RootElement.GetProperty("mode").GetString().ShouldBe("FULL");
        details.RootElement.GetProperty("entryId").GetString().ShouldBe(entryId.ToString());
        details.RootElement.GetProperty("accountVersion").GetInt64().ShouldBe(7);
    }

    [DockerFact]
    public async Task TryBeginRun_WhileTheSameModeIsOpen_ReturnsNullAndTheOtherModeStillWorks()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);

        var recent = await harness.Sessions.TryBeginRunAsync(IntegrityMode.Recent, CancellationToken.None);

        recent.ShouldNotBeNull();
        (await harness.Sessions.TryBeginRunAsync(IntegrityMode.Recent, CancellationToken.None)).ShouldBeNull();

        var full = await harness.Sessions.TryBeginRunAsync(IntegrityMode.Full, CancellationToken.None);

        full.ShouldNotBeNull();

        await recent.DisposeAsync();

        var again = await harness.Sessions.TryBeginRunAsync(IntegrityMode.Recent, CancellationToken.None);

        again.ShouldNotBeNull();

        await again.DisposeAsync();
        await full.DisposeAsync();
    }

    [DockerFact]
    public async Task TheLockAndTheQueriesOfARun_RunOnTheSameBackendConnection()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);

        var run = await harness.Sessions.TryBeginRunAsync(IntegrityMode.Recent, CancellationToken.None);

        run.ShouldNotBeNull();

        await using var admin = await harness.Database.OpenAdministrativeConnectionAsync(CancellationToken.None);

        var holders = await HoldersAsync(admin);
        var busyBackends = await ApplicationBackendsAsync(admin);

        holders.Count.ShouldBe(1);
        busyBackends.ShouldContain(holders[0]);

        await run.GetDatabaseNowAsync(CancellationToken.None);

        (await HoldersAsync(admin)).ShouldBe(holders);

        await run.DisposeAsync();

        (await HoldersAsync(admin)).ShouldBeEmpty();
    }

    [DockerFact]
    public async Task DisposingARunSession_ExplicitlyReleasesTheAdvisoryLockEvenIfTheConnectionIsPooled()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);
        await using var admin = await harness.Database.OpenAdministrativeConnectionAsync(CancellationToken.None);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var run = await harness.Sessions.TryBeginRunAsync(IntegrityMode.Recent, CancellationToken.None);

            run.ShouldNotBeNull();

            await run.DisposeAsync();

            (await HoldersAsync(admin)).ShouldBeEmpty();
        }
    }

    [DockerFact]
    public async Task TheChainQuery_UsesNestedLoopsWithIndexLookupsAndNeverAHashOrMergeJoin()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);

        for (var index = 0; index < 30; index++)
        {
            await harness.Ledger.SeedAsync();
        }

        await using var admin = await harness.Database.OpenAdministrativeConnectionAsync(CancellationToken.None);

        await ExecuteAsync(admin, "ANALYZE ledger_entries");

        var window = new[]
        {
            ("window_start", DateTimeOffset.UnixEpoch),
            ("window_end", DateTimeOffset.UnixEpoch.AddYears(200))
        };

        var planner = await ExplainAsync(admin, PostgresIntegritySession.CheckChainSql, window);

        await ExecuteAsync(admin, "SET enable_seqscan = off");

        var forcedToIndexes = await ExplainAsync(admin, PostgresIntegritySession.CheckChainSql, window);

        planner.ShouldContain("Nested Loop");
        planner.ShouldNotContain("Hash Join");
        planner.ShouldNotContain("Merge Join");
        forcedToIndexes.ShouldContain("uq_ledger_entries_account_id_account_version");
        forcedToIndexes.ShouldNotContain("Hash Join");
        forcedToIndexes.ShouldNotContain("Merge Join");
    }

    private static async Task<List<int>> HoldersAsync(NpgsqlConnection admin)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pid FROM pg_locks WHERE locktype = 'advisory' AND classid = 727002 AND granted ORDER BY pid",
            admin);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        var pids = new List<int>();

        while (await reader.ReadAsync(CancellationToken.None))
        {
            pids.Add(reader.GetInt32(0));
        }

        return pids;
    }

    private static async Task<List<int>> ApplicationBackendsAsync(NpgsqlConnection admin)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pid FROM pg_stat_activity WHERE application_name = 'ledger-worker' ORDER BY pid",
            admin);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        var pids = new List<int>();

        while (await reader.ReadAsync(CancellationToken.None))
        {
            pids.Add(reader.GetInt32(0));
        }

        return pids;
    }

    [SuppressMessage("Security", "CA2100", Justification = "Callers pass literal SQL.")]
    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);

        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    [SuppressMessage("Security", "CA2100", Justification = "Callers pass the SQL constants of the code under test.")]
    private static async Task<string> ExplainAsync(
        NpgsqlConnection connection,
        string sql,
        params (string Name, DateTimeOffset Value)[] parameters)
    {
        await using var command = new NpgsqlCommand("EXPLAIN " + sql, connection);

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, NpgsqlTypes.NpgsqlDbType.TimestampTz, value);
        }

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        var lines = new List<string>();

        while (await reader.ReadAsync(CancellationToken.None))
        {
            lines.Add(reader.GetString(0));
        }

        return string.Join('\n', lines);
    }
}
