using System.Globalization;
using System.Net;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Domain.Entries;

namespace Ledger.Api.IntegrationTests.Reads;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Concurrency")]
[Trait("Category", "Integration")]
public sealed class StableAsOfTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const int History = 100;
    private const int Writes = 100;

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
    public async Task Balance_AsOfInsideTheOldHistory_IsIdenticalBeforeAndAfterHundredNewWrites()
    {
        await ConcurrencySettings.RepeatAsync(async () =>
        {
            var accountId = await _world.Host.CreateAccountAsync();
            var start = ReadClock.UtcNow.AddHours(-5);
            await _world.Ledger.SeedChainAsync(accountId, History, start, TimeSpan.FromMinutes(1));
            var asOf = start.AddMinutes(40);

            using var before = await _world.Client.BalanceAtAsync(accountId, asOf);

            var results = await ParallelGate.RunAsync(
                Writes,
                index => _world.Host.RegisterAsync(
                    accountId,
                    $"stable-{Guid.CreateVersion7():N}-{index}",
                    EntryType.Credit,
                    1.00m + (index % 3)));

            using var after = await _world.Client.BalanceAtAsync(accountId, asOf);
            using var afterInBrasilia = await _world.Client.BalanceAsOfAsync(
                accountId,
                asOf.ToOffset(TimeSpan.FromHours(-3)).ToString("yyyy-MM-dd'T'HH:mm:ss.ffffffzzz", CultureInfo.InvariantCulture));

            results.ShouldAllBe(result => result.IsSuccess);
            before.Status.ShouldBe(HttpStatusCode.OK, before.Body);
            before.Json.GetProperty("settled").GetBoolean().ShouldBeTrue();
            after.Body.ShouldBe(before.Body);
            afterInBrasilia.Body.ShouldBe(before.Body);
            after.Json.GetProperty("settled").GetBoolean().ShouldBeTrue();
        });
    }
}
