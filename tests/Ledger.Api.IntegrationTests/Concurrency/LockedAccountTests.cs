using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Concurrency;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Concurrency")]
[Trait("Category", "Integration")]
public sealed class LockedAccountTests(PostgresFixture postgres)
{
    [DockerFact]
    public async Task ALockedAccount_DoesNotBlockTheOthers_AndItsOwnEntryCompletesAfterTheRelease()
    {
        await using var host = LedgerHost.Create(postgres, LedgerHost.WideWritePool);
        var queries = new LedgerQueries(postgres);
        var locked = await host.CreateFundedAccountAsync(100.00m);
        var free = await host.CreateFundedAccountAsync(100.00m);

        await using var holder = await postgres.OpenConnectionAsync(CancellationToken.None);
        await using var transaction = await holder.BeginTransactionAsync(CancellationToken.None);

        await HoldBalanceRowAsync(holder, transaction, locked);

        var blocked = host.RegisterAsync(locked, "blocked-key", EntryType.Debit, 10.00m);
        var passing = host.RegisterAsync(free, "free-key", EntryType.Debit, 10.00m);

        var first = await Task.WhenAny(blocked, passing);

        first.ShouldBeSameAs(passing);
        (await passing).IsSuccess.ShouldBeTrue();

        await BlockedBackends.UntilAnyAsync(postgres, "the entry on the locked account to wait for its lock");
        blocked.IsCompleted.ShouldBeFalse();

        await transaction.CommitAsync(CancellationToken.None);

        (await blocked).IsSuccess.ShouldBeTrue();
        (await queries.BalanceRowAsync(locked)).Balance.ShouldBe(90.00m);
        (await queries.BalanceRowAsync(free)).Balance.ShouldBe(90.00m);
    }

    private static async Task HoldBalanceRowAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, AccountId accountId)
    {
        await using var command = new NpgsqlCommand(
            "SELECT 1 FROM account_balances WHERE account_id = @account_id FOR UPDATE",
            connection,
            transaction);

        command.Parameters.AddWithValue("account_id", accountId.Value);

        await command.ExecuteScalarAsync(CancellationToken.None);
    }
}
