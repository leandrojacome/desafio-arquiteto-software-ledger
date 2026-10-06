using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Infrastructure.Persistence;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Persistence;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class QueryPlanTests(PostgresFixture postgres)
{
    private const int EntryCount = 100_000;
    private const int PageSize = 50;
    private const int AllowedExtraBlocks = 10;
    private const int CommandTimeoutSeconds = 120;
    private const int MaxVacuumPasses = 20;
    private const int PauseBetweenPassesMilliseconds = 250;
    private const string EntriesTable = "ledger_entries";
    private const string EntriesIndex = "ix_ledger_entries_account_id_recorded_at_account_version";

    private static readonly ConditionalWeakTable<PostgresFixture, Lazy<Task<History>>> Histories = new();

    private static readonly DateTimeOffset Start = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly Parameter[] StatementParameters =
    [
        new("account_id", "uuid"),
        new("from", "timestamptz"),
        new("upper_recorded_at", "timestamptz"),
        new("upper_account_version", "bigint"),
        new("limit_plus_one", "integer")
    ];

    private static readonly Parameter[] BalanceAtParameters =
    [
        new("account_id", "uuid"),
        new("as_of", "timestamptz")
    ];

    private static readonly Parameter[] CurrentBalanceParameters = [new("account_id", "uuid")];

    [DockerFact]
    public async Task BalanceAsOf_OnAHundredThousandEntries_UsesAnIndexOnlyScanWithoutHeapFetches()
    {
        var history = await HistoryAsync();
        await using var connection = await OpenAsync(history);

        var plan = await ExplainAsync(
            connection,
            PostgresBalanceReader.ReadBalanceAtSql,
            BalanceAtParameters,
            [Uuid(history.AccountId.Value), Instant(Moment(EntryCount / 2))]);

        var scan = plan.On(EntriesTable).ShouldHaveSingleItem();

        scan.Type.ShouldBe("Index Only Scan");
        scan.Index.ShouldBe(EntriesIndex);
        scan.HeapFetches.ShouldBe(0);
        plan.Has("Seq Scan").ShouldBeFalse(plan.ToString());
        plan.Has("Sort").ShouldBeFalse(plan.ToString());
        plan.Has("Bitmap Heap Scan").ShouldBeFalse(plan.ToString());
    }

    [DockerFact]
    public async Task BalanceAsOf_AtTheEdgesOfTheHistory_StillUsesTheIndexOnlyScan()
    {
        var history = await HistoryAsync();
        await using var connection = await OpenAsync(history);

        foreach (var asOf in new[] { Moment(1), Moment(EntryCount), Moment(EntryCount).AddDays(30) })
        {
            var plan = await ExplainAsync(
                connection,
                PostgresBalanceReader.ReadBalanceAtSql,
                BalanceAtParameters,
                [Uuid(history.AccountId.Value), Instant(asOf)]);

            plan.On(EntriesTable).ShouldHaveSingleItem().Type.ShouldBe("Index Only Scan");
        }
    }

    [DockerTheory]
    [InlineData("top")]
    [InlineData("to-in-the-first-tenth")]
    [InlineData("cursor-in-the-middle")]
    public async Task Statement_FirstPageOfEachShape_WalksTheIndexInOrderWithoutSortingOrScanningTheTable(string shape)
    {
        var history = await HistoryAsync();
        await using var connection = await OpenAsync(history);

        var plan = await ExplainStatementAsync(connection, history, shape);

        var scan = plan.On(EntriesTable).ShouldHaveSingleItem();

        scan.Type.ShouldBeOneOf("Index Scan", "Index Only Scan");
        scan.Index.ShouldBe(EntriesIndex);
        plan.Has("Sort").ShouldBeFalse(plan.ToString());
        plan.Has("Seq Scan").ShouldBeFalse(plan.ToString());
        plan.Has("Bitmap Heap Scan").ShouldBeFalse(plan.ToString());
    }

    [DockerFact]
    public async Task Statement_ReadsAboutTheSameNumberOfBlocksWhateverTheDepthOfThePage()
    {
        var history = await HistoryAsync();
        await using var connection = await OpenAsync(history);

        var top = await ExplainStatementAsync(connection, history, "top");
        var deepWindow = await ExplainStatementAsync(connection, history, "to-in-the-first-tenth");
        var deepCursor = await ExplainStatementAsync(connection, history, "cursor-in-the-middle");

        deepWindow.SharedBlocks.ShouldBeLessThanOrEqualTo(top.SharedBlocks + AllowedExtraBlocks);
        deepCursor.SharedBlocks.ShouldBeLessThanOrEqualTo(top.SharedBlocks + AllowedExtraBlocks);
    }

    [DockerFact]
    public async Task CurrentBalance_UsesThePrimaryKeysOfAccountsAndBalances()
    {
        var history = await HistoryAsync();
        await using var connection = await OpenAsync(history);

        await ExecuteAsync(connection, "SET enable_seqscan = off");

        var plan = await ExplainAsync(
            connection,
            PostgresBalanceReader.ReadCurrentBalanceSql,
            CurrentBalanceParameters,
            [Uuid(history.AccountId.Value)]);

        var accounts = plan.On("accounts").ShouldHaveSingleItem();
        var balances = plan.On("account_balances").ShouldHaveSingleItem();

        accounts.Type.ShouldBe("Index Scan");
        accounts.Index.ShouldBe("pk_accounts");
        balances.Type.ShouldBe("Index Scan");
        balances.Index.ShouldBe("pk_account_balances");
        plan.Has("Seq Scan").ShouldBeFalse(plan.ToString());
    }

    [Fact]
    public void ThePreparedText_ReplacesEveryNamedParameterByItsPosition()
    {
        var prepared = Prepare("SELECT @account_id, @from, @upper_account_version", StatementParameters.Take(4).ToArray());

        prepared.ShouldBe("SELECT $1, $2, $4");
    }

    [Fact]
    public void TheStatementText_UsesOnlyTheParametersTheTestBinds()
    {
        var unbound = System.Text.RegularExpressions.Regex
            .Matches(PostgresStatementReader.ReadStatementPageSql, "@[a-z_]+")
            .Select(match => match.Value[1..])
            .Distinct()
            .Except(StatementParameters.Select(parameter => parameter.Name))
            .ToList();

        unbound.ShouldBeEmpty();
    }

    private static string Prepare(string sql, Parameter[] parameters)
    {
        var text = sql;

        for (var index = 0; index < parameters.Length; index++)
        {
            text = text.Replace($"@{parameters[index].Name}", $"${index + 1}", StringComparison.Ordinal);
        }

        return text;
    }

    private static DateTimeOffset Moment(int version) => Start.AddSeconds(version - 1);

    private static string Uuid(Guid value) => $"'{value}'::uuid";

    private static string Instant(DateTimeOffset value) =>
        $"'{value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)}'::timestamptz";

    private static string Null(string type) => $"NULL::{type}";

    private static string Number(long value, string type) => $"({value.ToString(CultureInfo.InvariantCulture)})::{type}";

    private async Task<History> HistoryAsync()
    {
        var lazy = Histories.GetValue(postgres, fixture => new Lazy<Task<History>>(() => SeedAsync(fixture)));

        return await lazy.Value;
    }

    [SuppressMessage("Reliability", "CA2000",
        Justification = "The database lives as long as the fixture container and is dropped with it.")]
    private static async Task<History> SeedAsync(PostgresFixture fixture)
    {
        var database = await fixture.CreateEmptyDatabaseAsync(CancellationToken.None);

        (await database.MigrateAsync(CancellationToken.None)).Succeeded.ShouldBeTrue();

        await using var connection = await database.OpenAdministrativeConnectionAsync(CancellationToken.None);

        await ExecuteAsync(connection, "CREATE EXTENSION pg_visibility");

        var accountId = AccountId.From(Guid.CreateVersion7()).Value;

        await LedgerSeeder.SeedChainAsync(connection, accountId, EntryCount, Start, TimeSpan.FromSeconds(1));
        await VacuumUntilEveryPageIsAllVisibleAsync(connection);

        return new History(database, accountId);
    }

    private static async Task<NpgsqlConnection> OpenAsync(History history)
    {
        var connection = await history.Database.OpenAdministrativeConnectionAsync(CancellationToken.None);

        await ExecuteAsync(connection, "SET plan_cache_mode = force_generic_plan");

        return connection;
    }

    private static async Task<QueryPlan> ExplainStatementAsync(NpgsqlConnection connection, History history, string shape)
    {
        var values = shape switch
        {
            "top" => StatementValues(history, null, null),
            "to-in-the-first-tenth" => StatementValues(history, StatementPosition.Before(Moment(EntryCount / 10)), null),
            "cursor-in-the-middle" => StatementValues(
                history,
                new StatementPosition(Moment(EntryCount / 2), EntryCount / 2),
                null),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unknown statement shape.")
        };

        return await ExplainAsync(connection, PostgresStatementReader.ReadStatementPageSql, StatementParameters, values);
    }

    private static string[] StatementValues(History history, StatementPosition? upper, DateTimeOffset? from)
    {
        return
        [
            Uuid(history.AccountId.Value),
            from is { } lower ? Instant(lower) : Null("timestamptz"),
            upper is { } position ? Instant(position.RecordedAt) : Null("timestamptz"),
            upper is { } bound ? Number(bound.AccountVersion, "bigint") : Null("bigint"),
            Number(PageSize + 1, "integer")
        ];
    }

    [SuppressMessage("Security", "CA2100",
        Justification = "The statement is a constant of the production reader; the substituted values are literals built by this class.")]
    private static async Task<QueryPlan> ExplainAsync(
        NpgsqlConnection connection,
        string sql,
        Parameter[] parameters,
        string[] values)
    {
        var name = "plan_" + Guid.NewGuid().ToString("N");
        var types = string.Join(", ", parameters.Select(parameter => parameter.Type));
        var script = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"PREPARE {name} ({types}) AS {Prepare(sql, parameters)}");

        await ExecuteAsync(connection, script.ToString());

        await using var command = new NpgsqlCommand(
            $"EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) EXECUTE {name}({string.Join(", ", values)})",
            connection);

        command.CommandTimeout = CommandTimeoutSeconds;

        var text = (string)(await command.ExecuteScalarAsync(CancellationToken.None))!;

        return QueryPlan.Parse(text);
    }

    private static async Task VacuumUntilEveryPageIsAllVisibleAsync(NpgsqlConnection connection)
    {
        VisibilitySummary summary = new(0, 0);

        for (var pass = 0; pass < MaxVacuumPasses; pass++)
        {
            await ExecuteAsync(connection, "VACUUM (FREEZE, ANALYZE, DISABLE_PAGE_SKIPPING) ledger_entries");

            summary = await ReadVisibilityAsync(connection);

            if (summary.Pages > 0 && summary.AllVisible == summary.Pages)
            {
                return;
            }

            await Task.Delay(PauseBetweenPassesMilliseconds);
        }

        throw new InvalidOperationException(await DescribeHorizonAsync(connection, summary));
    }

    private static async Task<VisibilitySummary> ReadVisibilityAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT (SELECT all_visible FROM pg_visibility_map_summary('ledger_entries')),
                   pg_relation_size('ledger_entries') / current_setting('block_size')::bigint
            """,
            connection);

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        (await reader.ReadAsync(CancellationToken.None)).ShouldBeTrue();

        return new VisibilitySummary(reader.GetInt64(0), reader.GetInt64(1));
    }

    private static async Task<string> DescribeHorizonAsync(NpgsqlConnection connection, VisibilitySummary summary)
    {
        var text = new StringBuilder();

        text.Append(CultureInfo.InvariantCulture, $"Only {summary.AllVisible} of {summary.Pages} pages became all-visible after {MaxVacuumPasses} passes of VACUUM. ");
        text.Append("Sessions holding the transaction horizon: ");

        await using var command = new NpgsqlCommand(
            """
            SELECT pid, state, backend_xmin::text, application_name
            FROM pg_stat_activity
            WHERE datname = current_database() AND backend_xmin IS NOT NULL AND pid <> pg_backend_pid()
            """,
            connection);

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        while (await reader.ReadAsync(CancellationToken.None))
        {
            text.Append(CultureInfo.InvariantCulture, $"[pid {reader.GetInt32(0)} {reader.GetString(1)} xmin {reader.GetString(2)} {reader.GetString(3)}] ");
        }

        return text.ToString();
    }

    [SuppressMessage("Security", "CA2100",
        Justification = "Test helper: every SQL text passed in is built from constants written in this file.")]
    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);

        command.CommandTimeout = CommandTimeoutSeconds;

        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private sealed record Parameter(string Name, string Type);

    private sealed record VisibilitySummary(long AllVisible, long Pages);

    private sealed record History(EmptyDatabase Database, AccountId AccountId);
}
