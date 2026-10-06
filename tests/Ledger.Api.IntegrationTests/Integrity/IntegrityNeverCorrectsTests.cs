using System.Diagnostics.CodeAnalysis;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application.Integrity;
using Ledger.Infrastructure.Persistence;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Integrity;

[Collection(MessagingCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class IntegrityNeverCorrectsTests(PostgresFixture postgres)
{
    [DockerFact]
    public async Task RunsOverTamperedAccounts_NeverChangeALedgerBalanceOrOutboxRow()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);
        var first = await harness.Ledger.SeedAsync();
        var second = await harness.Ledger.SeedAsync();

        await harness.Ledger.SeedAsync();
        await harness.Ledger.TamperStoredBalanceAsync(first.AccountId, 3.00m);
        await harness.Ledger.TamperBalanceAfterAsync(second.Entries[2].Id, 1.00m);

        var before = await harness.Ledger.FingerprintAsync();

        var recent = await harness.RunAsync(IntegrityMode.Recent);
        var full = await harness.RunAsync(IntegrityMode.Full);

        var after = await harness.Ledger.FingerprintAsync();

        recent.Violations.ShouldBe(3);
        full.Violations.ShouldBe(3);
        after.ShouldBe(before);
    }

    [DockerFact]
    public async Task TheWorkerRole_CannotWriteTheLedgerNorTheBalances()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);
        var account = await harness.Ledger.SeedAsync();

        await using var worker = await OpenAsWorkerAsync(harness);

        var insert = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(
            worker,
            "INSERT INTO ledger_entries (id, account_id, account_version, type, amount, currency, balance_after, recorded_at, occurred_at, client_id, correlation_id) VALUES (gen_random_uuid(), @id, 99, 'CREDIT', 1, 'BRL', 1, now(), now(), 'x', 'x')",
            account.AccountId));
        var update = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(
            worker,
            "UPDATE ledger_entries SET balance_after = 0 WHERE account_id = @id",
            account.AccountId));
        var delete = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(
            worker,
            "DELETE FROM ledger_entries WHERE account_id = @id",
            account.AccountId));
        var balances = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(
            worker,
            "UPDATE account_balances SET balance = 0 WHERE account_id = @id",
            account.AccountId));

        new[] { insert, update, delete, balances }.ShouldAllBe(
            exception => exception.SqlState == PostgresErrorCodes.InsufficientPrivilege);
    }

    private static async Task<NpgsqlConnection> OpenAsWorkerAsync(IntegrityHarness harness)
    {
        var connection = new NpgsqlConnection(PostgresConnectionString.Build(harness.Database.Settings, PostgresSource.Worker));

        await connection.OpenAsync(CancellationToken.None);

        return connection;
    }

    [SuppressMessage("Security", "CA2100", Justification = "Callers pass literal SQL.")]
    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, Guid accountId)
    {
        await using var command = new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue("id", accountId);

        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }
}
