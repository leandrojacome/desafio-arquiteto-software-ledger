using System.Net;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Reads;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Concurrency")]
[Trait("Category", "Integration")]
public sealed class ReadsDuringWritesTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const int Writes = 100;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(5);

    private ReadWorld _world = null!;

    public Task InitializeAsync()
    {
        _world = ReadWorld.Create(postgres, hostOverrides: LedgerHost.WideWritePool);

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _world.DisposeAsync();
    }

    [DockerFact]
    public async Task Balance_ReadEveryFiveMillisecondsDuringHundredWrites_AlwaysMatchesACommittedEntryAndNeverGoesBack()
    {
        await ConcurrencySettings.RepeatAsync(async () =>
        {
            var accountId = await _world.Host.CreateFundedAccountAsync(100_000.00m);
            using var stop = new CancellationTokenSource();
            var samples = new List<(string Balance, string LastEntryId)>();

            var poller = Task.Run(
                async () =>
                {
                    while (!stop.IsCancellationRequested)
                    {
                        using var response = await _world.Client.BalanceAsync(accountId);

                        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
                        samples.Add((response.Text("balance"), response.Text("lastEntryId")));

                        await Task.Delay(PollInterval, CancellationToken.None);
                    }
                },
                CancellationToken.None);

            var results = await ParallelGate.RunAsync(
                Writes,
                index => _world.Host.RegisterAsync(
                    accountId,
                    $"during-{Guid.CreateVersion7():N}-{index}",
                    index % 3 == 0 ? EntryType.Debit : EntryType.Credit,
                    1.00m + (index % 7)));

            await stop.CancelAsync();
            await poller;

            results.ShouldAllBe(result => result.IsSuccess);

            var rows = (await _world.Queries.EntriesAsync(accountId)).ToDictionary(row => row.Id.ToString());
            var versions = new List<long>();

            samples.Count.ShouldBeGreaterThan(0);

            foreach (var (balance, lastEntryId) in samples)
            {
                rows.ContainsKey(lastEntryId).ShouldBeTrue();
                rows[lastEntryId].BalanceAfter.ToString("F2", System.Globalization.CultureInfo.InvariantCulture).ShouldBe(balance);
                versions.Add(rows[lastEntryId].AccountVersion);
            }

            versions.ShouldBe([.. versions.Order()]);
            await InvariantVerifier.AssertAccountIsConsistentAsync(postgres.AdministrativeSource, accountId.Value, CancellationToken.None);
        });
    }

    [DockerFact]
    public async Task Reads_WhileAWriteWaitsForTheLockOfTheAccount_AnswerWithoutWaiting()
    {
        var accountId = await _world.Host.CreateFundedAccountAsync(100.00m);

        await using var locker = await postgres.AdministrativeSource.OpenConnectionAsync();
        await using var transaction = await locker.BeginTransactionAsync();

        await using (var lockRow = new NpgsqlCommand(
                         "SELECT balance FROM account_balances WHERE account_id = @account_id FOR UPDATE",
                         locker,
                         transaction))
        {
            lockRow.Parameters.AddWithValue("account_id", accountId.Value);
            await lockRow.ExecuteScalarAsync();
        }

        var waitingWrite = Task.Run(
            () => _world.Host.RegisterAsync(accountId, "waits-for-the-lock", EntryType.Credit, 1.00m),
            CancellationToken.None);

        using var balance = await _world.Client.BalanceAsync(accountId);
        using var historical = await _world.Client.BalanceAtAsync(accountId, ReadClock.UtcNow);
        using var statement = await _world.Client.StatementAsync(accountId);

        var writeFinishedEarly = waitingWrite.IsCompleted;

        await transaction.CommitAsync();

        var written = await waitingWrite;

        balance.Status.ShouldBe(HttpStatusCode.OK, balance.Body);
        historical.Status.ShouldBe(HttpStatusCode.OK, historical.Body);
        statement.Status.ShouldBe(HttpStatusCode.OK, statement.Body);
        balance.Text("balance").ShouldBe("100.00");
        writeFinishedEarly.ShouldBeFalse();
        written.IsSuccess.ShouldBeTrue();
    }

    [DockerFact]
    public async Task Balance_OfManyAccountsRead_AfterTheirConcurrentWrites_MatchesTheLastEntryOfEach()
    {
        var accounts = new List<AccountId>();

        for (var index = 0; index < 10; index++)
        {
            accounts.Add(await _world.Host.CreateFundedAccountAsync(500.00m));
        }

        await ParallelGate.RunAsync(
            accounts.Count * 5,
            index => _world.Host.RegisterAsync(accounts[index % accounts.Count], $"many-{Guid.CreateVersion7():N}", EntryType.Credit, 2.00m));

        foreach (var accountId in accounts)
        {
            using var balance = await _world.Client.BalanceAsync(accountId);
            using var statement = await _world.Client.StatementAsync(accountId, "limit=1");

            balance.Money("balance").ShouldBe(510.00m);
            StatementItem.ItemsOf(statement).Single().EntryId.ShouldBe(balance.Text("lastEntryId"));
        }
    }
}
