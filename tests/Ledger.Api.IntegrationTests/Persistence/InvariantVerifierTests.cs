using System.Diagnostics.CodeAnalysis;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Domain.Entries;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Persistence;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class InvariantVerifierTests(PostgresFixture postgres)
{
    [DockerFact]
    public async Task HealthyAccount_HasNoViolations()
    {
        await using var ledger = LedgerHost.Create(postgres);
        var accountId = await CreateAccountWithThreeEntriesAsync(ledger);

        var violations = await InvariantVerifier.ViolationsAsync(postgres.AdministrativeSource, accountId, CancellationToken.None);

        violations.ShouldBeEmpty();
    }

    [DockerFact]
    public async Task AlteredBalanceAfter_BreaksTheChainAndTheLastEntryCheck()
    {
        await using var ledger = LedgerHost.Create(postgres);
        var accountId = await CreateAccountWithThreeEntriesAsync(ledger);

        await CorruptAsync(
            "UPDATE ledger_entries SET balance_after = balance_after + 1 WHERE account_id = @account_id AND account_version = 3",
            accountId);

        var violations = await InvariantVerifier.ViolationsAsync(postgres.AdministrativeSource, accountId, CancellationToken.None);

        violations.Keys.ShouldBe(
            [InvariantVerifier.BalanceMatchesLastEntryAndFloor, InvariantVerifier.BalanceAfterChainCloses],
            ignoreOrder: true);
    }

    [DockerFact]
    public async Task AlteredCurrentBalance_BreaksTheSumAndTheLastEntryCheck()
    {
        await using var ledger = LedgerHost.Create(postgres);
        var accountId = await CreateAccountWithThreeEntriesAsync(ledger);

        await CorruptAsync("UPDATE account_balances SET balance = balance + 1 WHERE account_id = @account_id",
            accountId);

        var violations = await InvariantVerifier.ViolationsAsync(postgres.AdministrativeSource, accountId, CancellationToken.None);

        violations.Keys.ShouldBe(
            [InvariantVerifier.BalanceEqualsSumOfEntries, InvariantVerifier.BalanceMatchesLastEntryAndFloor],
            ignoreOrder: true);
    }

    [DockerFact]
    public async Task RecordedAtMovedBack_BreaksTheSequenceOnly()
    {
        await using var ledger = LedgerHost.Create(postgres);
        var accountId = await CreateAccountWithThreeEntriesAsync(ledger);

        await CorruptAsync(
            "UPDATE ledger_entries SET recorded_at = recorded_at - INTERVAL '1 day' WHERE account_id = @account_id AND account_version = 2",
            accountId);

        var violations = await InvariantVerifier.ViolationsAsync(postgres.AdministrativeSource, accountId, CancellationToken.None);

        violations.Keys.ShouldBe([InvariantVerifier.VersionAndRecordedAtSequence]);
    }

    private static async Task<Guid> CreateAccountWithThreeEntriesAsync(LedgerHost ledger)
    {
        var accountId = await ledger.CreateFundedAccountAsync(100.00m);

        await ledger.RegisterAsync(accountId, "debit-30", EntryType.Debit, 30.00m);
        await ledger.RegisterAsync(accountId, "credit-5", EntryType.Credit, 5.00m);

        return accountId.Value;
    }

    [SuppressMessage("Security", "CA2100",
        Justification = "Test helper: every SQL text passed in is a constant literal written in this file.")]
    private async Task CorruptAsync(string sql, Guid accountId)
    {
        await using var connection = await postgres.OpenConnectionAsync(CancellationToken.None);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken.None);

        await using (var relax =
                     new NpgsqlCommand("SET LOCAL session_replication_role = replica", connection, transaction))
        {
            await relax.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await using var update = new NpgsqlCommand(sql, connection, transaction);

        update.Parameters.AddWithValue("account_id", accountId);

        await update.ExecuteNonQueryAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
    }
}
