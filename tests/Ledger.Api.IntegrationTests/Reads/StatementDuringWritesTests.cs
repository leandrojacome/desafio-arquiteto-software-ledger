using System.Net;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Domain.Entries;

namespace Ledger.Api.IntegrationTests.Reads;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Concurrency")]
[Trait("Category", "Integration")]
public sealed class StatementDuringWritesTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const int Existing = 300;
    private const int Writes = 100;
    private const int PageSize = 50;

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
    public async Task Statement_PaginatedWhileHundredWritesArrive_ReturnsExactlyTheEntriesThatExistedAtTheFirstRead()
    {
        await ConcurrencySettings.RepeatAsync(async () =>
        {
            var accountId = await _world.Host.CreateAccountAsync();
            await _world.Ledger.SeedChainAsync(accountId, Existing, ReadClock.UtcNow.AddHours(-6), TimeSpan.FromSeconds(1));

            using var first = await _world.Client.StatementAsync(accountId, $"limit={PageSize}");

            first.Status.ShouldBe(HttpStatusCode.OK, first.Body);

            var results = await ParallelGate.RunAsync(
                Writes,
                index => _world.Host.RegisterAsync(
                    accountId,
                    $"statement-during-{Guid.CreateVersion7():N}-{index}",
                    index % 2 == 0 ? EntryType.Credit : EntryType.Debit,
                    1.00m + (index % 5)));

            var rest = await _world.Client.WalkAsync(accountId, PageSize, startCursor: first.Text("nextCursor"));

            results.ShouldAllBe(result => result.IsSuccess);

            var seen = StatementItem.ItemsOf(first).Concat(rest.Items).ToList();
            var versions = seen.Select(item => item.AccountVersion).ToList();

            versions.ShouldBe(Enumerable.Range(1, Existing).Select(version => (long)version).Reverse());
            seen.Select(item => item.EntryId).Distinct().Count().ShouldBe(Existing);
            seen.Select(item => item.RecordedAtInstant).ShouldBe([.. seen.Select(item => item.RecordedAtInstant).OrderDescending()]);

            var fresh = await _world.Client.WalkAsync(accountId, 100);

            fresh.Items.Count.ShouldBe(Existing + Writes);
            fresh.Items.Take(Writes).Select(item => item.AccountVersion)
                .ShouldBe(Enumerable.Range(Existing + 1, Writes).Select(version => (long)version).Reverse());

            await InvariantVerifier.AssertAccountIsConsistentAsync(postgres.AdministrativeSource, accountId.Value, CancellationToken.None);
        });
    }
}
