using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Domain.Entries;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Persistence;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class CommitCuttingProxyTests(PostgresFixture postgres)
{
    [DockerFact]
    public async Task Proxy_RelaysAPlainQueryAndATransaction()
    {
        await using var proxy = CommitCuttingProxy.Start(postgres.Settings.Host, postgres.Settings.Port);
        await using var connection = await OpenAsync(proxy);

        await using var select = new NpgsqlCommand("SELECT 1", connection);
        (await select.ExecuteScalarAsync()).ShouldBe(1);

        var accountId = Guid.CreateVersion7();

        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await InsertAccountAsync(connection, transaction, accountId);
            await transaction.CommitAsync();
        }

        (await AccountExistsAsync(accountId)).ShouldBeTrue();
        proxy.CommitsCut.ShouldBe(0);
    }

    [DockerFact]
    public async Task Proxy_SeesTheCommitOfNpgsqlAsASimpleQuery()
    {
        await using var proxy = CommitCuttingProxy.Start(postgres.Settings.Host, postgres.Settings.Port);
        await using var connection = await OpenAsync(proxy);

        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await InsertAccountAsync(connection, transaction, Guid.CreateVersion7());
            await transaction.CommitAsync();
        }

        proxy.CommitMessageKinds.ShouldContain("simple-query");
    }

    [DockerFact]
    public async Task Proxy_CutsTheAnswerOfTheNextCommitOnceAndThePostgresStillCommits()
    {
        await using var proxy = CommitCuttingProxy.Start(postgres.Settings.Host, postgres.Settings.Port);
        var accountId = Guid.CreateVersion7();

        proxy.CutNextCommit();

        await using (var connection = await OpenAsync(proxy))
        {
            await using var transaction = await connection.BeginTransactionAsync();
            await InsertAccountAsync(connection, transaction, accountId);

            var cut = await Should.ThrowAsync<NpgsqlException>(() => transaction.CommitAsync());

            cut.IsTransient.ShouldBeTrue();
        }

        proxy.CommitsCut.ShouldBe(1);
        (await AccountExistsAsync(accountId)).ShouldBeTrue();

        var second = Guid.CreateVersion7();

        await using var another = await OpenAsync(proxy);
        await using (var transaction = await another.BeginTransactionAsync())
        {
            await InsertAccountAsync(another, transaction, second);
            await transaction.CommitAsync();
        }

        proxy.CommitsCut.ShouldBe(1);
        (await AccountExistsAsync(second)).ShouldBeTrue();
    }

    [DockerFact]
    public async Task Register_WhenTheAnswerOfTheCommitIsLost_TheInternalRetryReplaysTheCommittedEntry()
    {
        await using var proxy = CommitCuttingProxy.Start(postgres.Settings.Host, postgres.Settings.Port);
        await using var host = ThroughTheProxy(proxy);
        var queries = new LedgerQueries(postgres);
        var accountId = await host.CreateFundedAccountAsync(100.00m);

        proxy.CutNextCommit();

        var outcome = await host.RegisterAsync(accountId, "unknown-outcome", EntryType.Debit, 10.00m);

        var stored = (await queries.EntriesAsync(accountId))[^1];

        proxy.CommitsCut.ShouldBe(1);
        outcome.IsSuccess.ShouldBeTrue();
        outcome.Value.IsReplay.ShouldBeTrue();
        outcome.Value.Entry.Id.Value.ShouldBe(stored.Id);
        outcome.Value.Entry.BalanceAfter.Amount.ShouldBe(90.00m);
        (await queries.CountEntriesAsync(accountId)).ShouldBe(2);
        (await queries.CountOutboxAsync(accountId)).ShouldBe(2);
        (await queries.BalanceRowAsync(accountId)).Balance.ShouldBe(90.00m);
        host.Logs.Events.Count(log => log.EventId == 1006).ShouldBe(1);
    }

    [DockerFact]
    public async Task Register_WhenTheAnswerOfTheCommitIsLostAndRetriesAreOff_RepeatingTheCallReturnsTheSameEntry()
    {
        await using var proxy = CommitCuttingProxy.Start(postgres.Settings.Host, postgres.Settings.Port);
        await using var host = ThroughTheProxy(proxy, retries: 0);
        var queries = new LedgerQueries(postgres);
        var accountId = await host.CreateFundedAccountAsync(100.00m);

        proxy.CutNextCommit();

        await Should.ThrowAsync<NpgsqlException>(
            () => host.RegisterAsync(accountId, "unknown-outcome", EntryType.Debit, 10.00m));

        var repeated = await host.RegisterAsync(accountId, "unknown-outcome", EntryType.Debit, 10.00m);
        var stored = (await queries.EntriesAsync(accountId))[^1];

        repeated.Value.IsReplay.ShouldBeTrue();
        repeated.Value.Entry.Id.Value.ShouldBe(stored.Id);
        (await queries.CountEntriesAsync(accountId)).ShouldBe(2);
        (await queries.BalanceRowAsync(accountId)).Balance.ShouldBe(90.00m);
    }

    private LedgerHost ThroughTheProxy(CommitCuttingProxy proxy, int retries = 2)
    {
        var overrides = new Dictionary<string, string?>
        {
            ["Postgres:Host"] = "127.0.0.1",
            ["Postgres:Port"] = proxy.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Resilience:Retry:MaxRetryAttempts"] = retries.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        return LedgerHost.Create(postgres, overrides);
    }

    private static async Task<NpgsqlConnection> OpenAsync(CommitCuttingProxy proxy)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1",
            Port = proxy.Port,
            Database = PostgresFixture.Database,
            Username = PostgresFixture.ApiRole,
            Password = PostgresFixture.RolePassword,
            SslMode = SslMode.Disable,
            Pooling = false
        };

        var connection = new NpgsqlConnection(builder.ConnectionString);

        await connection.OpenAsync();

        return connection;
    }

    private static async Task InsertAccountAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id)
    {
        await using var command = new NpgsqlCommand("INSERT INTO accounts (id, currency) VALUES (@id, 'BRL')", connection, transaction);

        command.Parameters.AddWithValue("id", id);

        await command.ExecuteNonQueryAsync();
    }

    private async Task<bool> AccountExistsAsync(Guid id)
    {
        await using var command = postgres.AdministrativeSource.CreateCommand("SELECT EXISTS (SELECT 1 FROM accounts WHERE id = @id)");

        command.Parameters.AddWithValue("id", id);

        return (bool)(await command.ExecuteScalarAsync() ?? false);
    }
}
