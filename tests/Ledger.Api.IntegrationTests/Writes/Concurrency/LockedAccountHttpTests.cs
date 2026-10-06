using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Writes.Support;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Writes.Concurrency;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Concurrency")]
[Trait("Category", "Integration")]
public sealed class LockedAccountHttpTests(PostgresFixture postgres)
{
    private static readonly TimeSpan PendingWindow = TimeSpan.FromMilliseconds(700);

    private readonly WriteTestData _data = new(postgres);

    [DockerFact]
    public async Task EntryOnALockedAccount_WaitsWhileTheOtherAccountIsServedAndFinishesAfterTheRelease()
    {
        await using var factory = new WriteApiFactory(postgres, WriteApiFactory.WidePool);
        var client = new WriteClient(factory.CreateClient());
        var locked = await client.CreateFundedAccountAsync("100.00");
        var free = await client.CreateFundedAccountAsync("100.00");

        await using var holder = await postgres.OpenConnectionAsync(CancellationToken.None);
        await using var transaction = await holder.BeginTransactionAsync(CancellationToken.None);

        await using (var command = new NpgsqlCommand(
                         "SELECT 1 FROM account_balances WHERE account_id = @account_id FOR UPDATE",
                         holder,
                         transaction))
        {
            command.Parameters.AddWithValue("account_id", WriteTestData.Account(locked).Value);
            await command.ExecuteScalarAsync(CancellationToken.None);
        }

        var pending = client.DebitAsync(locked, "10.00");
        var served = client.DebitAsync(free, "10.00");

        (await Task.WhenAny(pending, served)).ShouldBe(served);
        (await served).StatusCode.ShouldBe(201);

        (await Task.WhenAny(pending, Task.Delay(PendingWindow))).ShouldNotBe(pending);

        await transaction.CommitAsync(CancellationToken.None);

        var finished = await pending;

        finished.StatusCode.ShouldBe(201, finished.Body);
        (await _data.BalanceAsync(locked)).ShouldBe(90.00m);
        await _data.AssertConsistentAsync(locked);
    }

    [DockerFact]
    public async Task LockHeldBeyondTheTimeout_Is503WithRetryAfterAndLeavesTheKeyFree()
    {
        var overrides = new Dictionary<string, string?>
        {
            ["Postgres:Sources:Write:LockTimeoutMs"] = "300"
        };
        await using var factory = new WriteApiFactory(postgres, overrides);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("100.00");
        var key = WriteClient.NewKey();

        await using var holder = await postgres.OpenConnectionAsync(CancellationToken.None);
        await using var transaction = await holder.BeginTransactionAsync(CancellationToken.None);

        await using (var command = new NpgsqlCommand(
                         "SELECT 1 FROM account_balances WHERE account_id = @account_id FOR UPDATE",
                         holder,
                         transaction))
        {
            command.Parameters.AddWithValue("account_id", WriteTestData.Account(accountId).Value);
            await command.ExecuteScalarAsync(CancellationToken.None);
        }

        var refused = await client.DebitAsync(accountId, "10.00", key);

        refused.ShouldBeProblem(503, "SERVICE_UNAVAILABLE");
        refused.Header("Retry-After").ShouldBe("1");

        await transaction.CommitAsync(CancellationToken.None);

        (await _data.CountEntriesAsync(accountId)).ShouldBe(1);
        (await _data.CountKeysAsync(accountId)).ShouldBe(1);
        (await _data.BalanceAsync(accountId)).ShouldBe(100.00m);

        var retried = await client.DebitAsync(accountId, "10.00", key);

        retried.StatusCode.ShouldBe(201, retried.Body);
        retried.HasHeader("Idempotent-Replayed").ShouldBeFalse();
        (await _data.CountEntriesAsync(accountId)).ShouldBe(2);
        (await _data.BalanceAsync(accountId)).ShouldBe(90.00m);
        await _data.AssertConsistentAsync(accountId);
    }
}
