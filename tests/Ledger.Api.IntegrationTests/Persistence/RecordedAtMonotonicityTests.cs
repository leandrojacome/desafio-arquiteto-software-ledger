using Ledger.Api.IntegrationTests.Concurrency;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Application.Entries;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;

namespace Ledger.Api.IntegrationTests.Persistence;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class RecordedAtMonotonicityTests(PostgresFixture postgres)
{
    private const int SequentialEntries = 50;
    private static readonly TimeSpan OneMicrosecond = TimeSpan.FromTicks(10);

    [DockerFact]
    public async Task RecordedAt_OfSequentialEntries_IsStrictlyIncreasingWithThePosition()
    {
        await using var host = LedgerHost.Create(postgres);
        var queries = new LedgerQueries(postgres);
        var accountId = await host.CreateAccountAsync();

        for (var index = 0; index < SequentialEntries; index++)
        {
            (await host.RegisterAsync(accountId, $"sequential-{index}", EntryType.Credit, 1.00m)).IsSuccess.ShouldBeTrue();
        }

        var entries = await queries.EntriesAsync(accountId);

        entries.Zip(entries.Skip(1), (earlier, later) => later.RecordedAt - earlier.RecordedAt)
            .ShouldAllBe(gap => gap > TimeSpan.Zero);
        entries.Select(entry => entry.AccountVersion)
            .ShouldBe(Enumerable.Range(1, SequentialEntries).Select(version => (long)version));
    }

    [DockerFact]
    public async Task RecordedAt_IsNeverEarlierThanTheCreationOfTheAccount()
    {
        await using var host = LedgerHost.Create(postgres);
        var queries = new LedgerQueries(postgres);
        var accountId = await host.CreateAccountAsync();
        var createdAt = (await queries.BalanceRowAsync(accountId)).LastRecordedAt;

        await host.RegisterAsync(accountId, "first", EntryType.Credit, 1.00m);

        (await queries.EntriesAsync(accountId)).Single().RecordedAt.ShouldBeGreaterThan(createdAt);
    }

    [DockerFact]
    public async Task RecordedAt_WhenTheStoredClockIsAhead_StaysStrictlyIncreasingByOneMicrosecond()
    {
        await using var host = LedgerHost.Create(postgres);
        var queries = new LedgerQueries(postgres);
        var accountId = await host.CreateAccountAsync();
        var databaseNow = await queries.DatabaseNowAsync();
        var earliestExpected = databaseNow + TimeSpan.FromHours(1);

        await queries.PushLastRecordedAtAheadAsync(accountId, TimeSpan.FromHours(1));

        var first = await ApplyAsync(host, accountId);
        var second = await ApplyAsync(host, accountId);
        var third = await ApplyAsync(host, accountId);

        first.Entry.RecordedAt.UtcDateTime.ShouldBeGreaterThan(earliestExpected);
        (second.Entry.RecordedAt - first.Entry.RecordedAt).ShouldBe(OneMicrosecond);
        (third.Entry.RecordedAt - second.Entry.RecordedAt).ShouldBe(OneMicrosecond);
        new[] { first, second, third }.ShouldAllBe(applied => applied.RecordedAtCorrected);
        await ConcurrencyScenarios.AssertConsistentAsync(postgres, accountId);
    }

    [DockerFact]
    public async Task RecordedAt_WhenTheClockIsNotAhead_IsNotMarkedAsCorrected()
    {
        await using var host = LedgerHost.Create(postgres);
        var accountId = await host.CreateAccountAsync();

        var applied = await ApplyAsync(host, accountId);

        applied.RecordedAtCorrected.ShouldBeFalse();
    }

    private static async Task<AppliedEntry> ApplyAsync(LedgerHost host, AccountId accountId)
    {
        var entry = Entry.Credit(
            EntryId.From(Guid.CreateVersion7()).Value,
            accountId,
            Money.CreatePositive(1.00m, LedgerHost.Currency).Value,
            null,
            null,
            null).Value;

        var applied = await host.ExecuteAsync(
            (scope, token) => scope.Entries.TryApplyAsync(
                new NewEntry(entry, LedgerHost.ClientId, LedgerHost.CorrelationId),
                token));

        return applied.Value;
    }
}
