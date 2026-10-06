using System.Diagnostics.CodeAnalysis;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application.Outbox;
using Ledger.Infrastructure.Persistence;
using Ledger.Infrastructure.Persistence.Outbox;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Outbox;

[Collection(MessagingCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresOutboxQueueTests(PostgresFixture postgres)
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);

    [DockerFact]
    public async Task ClaimBatch_ReturnsTheOldestMessagesFirstAndIncrementsAttemptsAndSetsTheLease()
    {
        await using var queueScenario = await QueueScenario.CreateAsync(postgres);
        var ids = await queueScenario.Seeder.InsertPendingAsync(10);

        var claimed = await queueScenario.Queue.ClaimBatchAsync(4, Lease, CancellationToken.None);
        var rows = await queueScenario.Seeder.ReadRowsAsync();

        claimed.Count.ShouldBe(4);
        claimed.Select(envelope => envelope.Id).ShouldBe(ids.Take(4), ignoreOrder: true);
        claimed.ShouldAllBe(envelope => envelope.Attempts == 1);
        rows.Where(row => ids.Take(4).Contains(row.Id)).ShouldAllBe(row => row.Attempts == 1 && row.LockedUntil != null);
        rows.Where(row => !ids.Take(4).Contains(row.Id)).ShouldAllBe(row => row.Attempts == 0 && row.LockedUntil == null);

        var lease = rows.First(row => row.Id == ids[0]).LockedUntil;
        var now = await queueScenario.Ledger.DatabaseNowAsync();

        lease.ShouldNotBeNull();
        (lease.Value - now).ShouldBeInRange(TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(30));
    }

    [DockerFact]
    public async Task ClaimBatch_ReturnsThePayloadAsTheTextOfTheJsonbColumnAndEveryEnvelopeField()
    {
        await using var queueScenario = await QueueScenario.CreateAsync(postgres);
        var accountId = Guid.NewGuid();

        await queueScenario.Seeder.InsertPendingAsync(1, accountId, withTraceParent: true);

        var envelope = (await queueScenario.Queue.ClaimBatchAsync(10, Lease, CancellationToken.None)).ShouldHaveSingleItem();
        var row = (await queueScenario.Seeder.ReadRowsAsync()).ShouldHaveSingleItem();

        envelope.Payload.ShouldBe(row.Payload);
        envelope.AccountId.Value.ShouldBe(accountId);
        envelope.Type.ShouldBe("EntryRegistered");
        envelope.TraceParent.ShouldBe("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");
        envelope.CreatedAt.ShouldBe(row.CreatedAt);
        envelope.CorrelationId.Length.ShouldBe(32);
    }

    [DockerFact]
    public async Task ClaimBatch_NeverReturnsPublishedMessagesNorMessagesWithALiveLease()
    {
        await using var queueScenario = await QueueScenario.CreateAsync(postgres);
        var ids = await queueScenario.Seeder.InsertPendingAsync(6);

        await queueScenario.Seeder.MarkPublishedAgoAsync(ids.Take(2).ToList(), TimeSpan.FromMinutes(1));

        var first = await queueScenario.Queue.ClaimBatchAsync(2, Lease, CancellationToken.None);
        var second = await queueScenario.Queue.ClaimBatchAsync(10, Lease, CancellationToken.None);

        first.Select(envelope => envelope.Id).ShouldBe(ids.Skip(2).Take(2), ignoreOrder: true);
        second.Select(envelope => envelope.Id).ShouldBe(ids.Skip(4), ignoreOrder: true);
        (await queueScenario.Queue.ClaimBatchAsync(10, Lease, CancellationToken.None)).ShouldBeEmpty();
    }

    [DockerFact]
    public async Task ClaimBatch_PicksUpAMessageAgainOnceItsLeaseHasExpired()
    {
        await using var queueScenario = await QueueScenario.CreateAsync(postgres);

        await queueScenario.Seeder.InsertPendingAsync(1);

        await queueScenario.Queue.ClaimBatchAsync(10, TimeSpan.FromMilliseconds(200), CancellationToken.None);

        await Task.Delay(TimeSpan.FromMilliseconds(600));

        var again = (await queueScenario.Queue.ClaimBatchAsync(10, Lease, CancellationToken.None)).ShouldHaveSingleItem();

        again.Attempts.ShouldBe(2);
    }

    [DockerFact]
    public async Task ClaimBatch_SkipsRowsLockedByAnotherTransactionInsteadOfWaiting()
    {
        await using var queueScenario = await QueueScenario.CreateAsync(postgres);
        var ids = await queueScenario.Seeder.InsertPendingAsync(200);

        await using var admin = await queueScenario.Database.OpenAdministrativeConnectionAsync(CancellationToken.None);
        await using var transaction = await admin.BeginTransactionAsync(CancellationToken.None);
        await using var locker = new NpgsqlCommand(
            "SELECT id FROM outbox_messages WHERE published_at IS NULL ORDER BY created_at LIMIT 100 FOR UPDATE",
            admin,
            transaction);

        var locked = new List<Guid>();

        await using (var reader = await locker.ExecuteReaderAsync(CancellationToken.None))
        {
            while (await reader.ReadAsync(CancellationToken.None))
            {
                locked.Add(reader.GetGuid(0));
            }
        }

        var claimed = await queueScenario.Queue.ClaimBatchAsync(100, Lease, CancellationToken.None);

        locked.Count.ShouldBe(100);
        claimed.Count.ShouldBe(100);
        claimed.Select(envelope => envelope.Id).Intersect(locked).ShouldBeEmpty();
        claimed.Select(envelope => envelope.Id).ShouldBe(ids.Skip(100), ignoreOrder: true);

        await transaction.RollbackAsync(CancellationToken.None);
    }

    [DockerFact]
    public async Task TwoWorkersClaimingTogether_NeverReceiveTheSameMessage()
    {
        await ConcurrencySettings.RepeatAsync(async () =>
        {
            await using var queueScenario = await QueueScenario.CreateAsync(postgres);

            await queueScenario.Seeder.InsertPendingAsync(400);

            var claims = await ParallelGate.RunAsync(
                8,
                _ => queueScenario.Queue.ClaimBatchAsync(100, Lease, CancellationToken.None));

            var all = claims.SelectMany(batch => batch.Select(envelope => envelope.Id)).ToList();

            all.Count.ShouldBe(400);
            all.Distinct().Count().ShouldBe(400);
        });
    }

    [DockerFact]
    public async Task MarkPublished_MarksOnlyTheGivenIdsAndClearsTheLease()
    {
        await using var queueScenario = await QueueScenario.CreateAsync(postgres);
        var ids = await queueScenario.Seeder.InsertPendingAsync(5);

        await queueScenario.Queue.ClaimBatchAsync(5, Lease, CancellationToken.None);

        var marked = await queueScenario.Queue.MarkPublishedAsync(ids.Take(3).ToList(), CancellationToken.None);
        var rows = await queueScenario.Seeder.ReadRowsAsync();

        marked.ShouldBe(3);
        rows.Where(row => ids.Take(3).Contains(row.Id)).ShouldAllBe(row => row.PublishedAt != null && row.LockedUntil == null);
        rows.Where(row => !ids.Take(3).Contains(row.Id)).ShouldAllBe(row => row.PublishedAt == null && row.LockedUntil != null);
    }

    [DockerFact]
    public async Task MarkPublished_WithNoIds_DoesNothing()
    {
        await using var queueScenario = await QueueScenario.CreateAsync(postgres);

        await queueScenario.Seeder.InsertPendingAsync(2);

        (await queueScenario.Queue.MarkPublishedAsync([], CancellationToken.None)).ShouldBe(0);
        (await queueScenario.Seeder.CountPendingAsync()).ShouldBe(2);
    }

    [DockerFact]
    public async Task Prune_RemovesOnlyMessagesPublishedLongerAgoThanTheRetention_InBatches()
    {
        await using var queueScenario = await QueueScenario.CreateAsync(postgres);
        var old = await queueScenario.Seeder.InsertPendingAsync(25);
        var recent = await queueScenario.Seeder.InsertPendingAsync(5);
        var pending = await queueScenario.Seeder.InsertPendingAsync(5);

        await queueScenario.Seeder.MarkPublishedAgoAsync(old, TimeSpan.FromDays(8));
        await queueScenario.Seeder.MarkPublishedAgoAsync(recent, TimeSpan.FromDays(6));

        var retention = TimeSpan.FromDays(7);

        (await queueScenario.Queue.PruneAsync(retention, 10, CancellationToken.None)).ShouldBe(10);
        (await queueScenario.Queue.PruneAsync(retention, 10, CancellationToken.None)).ShouldBe(10);
        (await queueScenario.Queue.PruneAsync(retention, 10, CancellationToken.None)).ShouldBe(5);
        (await queueScenario.Queue.PruneAsync(retention, 10, CancellationToken.None)).ShouldBe(0);

        var remaining = (await queueScenario.Seeder.ReadRowsAsync()).Select(row => row.Id).ToHashSet();

        remaining.ShouldBe(recent.Concat(pending).ToHashSet());
    }

    [DockerFact]
    public async Task ReadStats_OfAnEmptyOutbox_HasNoPendingMessageAndNoAge()
    {
        await using var queueScenario = await QueueScenario.CreateAsync(postgres);

        var stats = await queueScenario.Queue.ReadStatsAsync(new OutboxStatsRequest(1000, 5, 1000), CancellationToken.None);

        stats.Pending.ShouldBe(0);
        stats.OldestPendingAgeSeconds.ShouldBeNull();
        stats.Failed.ShouldBe(0);
    }

    [DockerFact]
    public async Task ReadStats_LimitsThePendingCountToTheCapAndReportsTheAgeOfTheOldest()
    {
        await using var queueScenario = await QueueScenario.CreateAsync(postgres);

        await queueScenario.Seeder.InsertPendingAsync(30);
        await Task.Delay(TimeSpan.FromMilliseconds(1200));

        var capped = await queueScenario.Queue.ReadStatsAsync(new OutboxStatsRequest(10, 5, 1000), CancellationToken.None);
        var full = await queueScenario.Queue.ReadStatsAsync(new OutboxStatsRequest(1000, 5, 1000), CancellationToken.None);

        capped.Pending.ShouldBe(10);
        full.Pending.ShouldBe(30);
        full.OldestPendingAgeSeconds.ShouldNotBeNull();
        full.OldestPendingAgeSeconds.Value.ShouldBeInRange(1d, 30d);
    }

    [DockerFact]
    public async Task ReadStats_CountsAsFailedTheMessagesAtTheThresholdAmongTheOldestPending()
    {
        await using var queueScenario = await QueueScenario.CreateAsync(postgres);

        await queueScenario.Seeder.InsertPendingAsync(10);

        for (var claim = 0; claim < 3; claim++)
        {
            await queueScenario.Queue.ClaimBatchAsync(2, TimeSpan.FromMilliseconds(50), CancellationToken.None);
            await Task.Delay(TimeSpan.FromMilliseconds(120));
        }

        var atTheThreshold = await queueScenario.Queue.ReadStatsAsync(new OutboxStatsRequest(1000, 3, 10), CancellationToken.None);
        var aboveTheThreshold = await queueScenario.Queue.ReadStatsAsync(new OutboxStatsRequest(1000, 4, 10), CancellationToken.None);
        var windowOfOne = await queueScenario.Queue.ReadStatsAsync(new OutboxStatsRequest(1000, 3, 1), CancellationToken.None);

        atTheThreshold.Failed.ShouldBe(2);
        aboveTheThreshold.Failed.ShouldBe(0);
        windowOfOne.Failed.ShouldBe(1);
    }

    private sealed class QueueScenario : IAsyncDisposable
    {
        private readonly PostgresConnectionFactory _factory;

        private QueueScenario(EmptyDatabase database, PostgresConnectionFactory factory)
        {
            Database = database;
            _factory = factory;
            Queue = new PostgresOutboxQueue(factory);
            Seeder = new OutboxSeeder(database);
            Ledger = new IntegrityLedger(database);
        }

        public EmptyDatabase Database { get; }

        public PostgresOutboxQueue Queue { get; }

        public OutboxSeeder Seeder { get; }

        public IntegrityLedger Ledger { get; }

        [SuppressMessage("Reliability", "CA2000",
            Justification = "Ownership of the database and the factory passes to the scenario, which disposes both.")]
        public static async Task<QueueScenario> CreateAsync(PostgresFixture postgres)
        {
            var database = await postgres.CreateEmptyDatabaseAsync(CancellationToken.None);

            (await database.MigrateAsync(CancellationToken.None)).Succeeded.ShouldBeTrue();

            return new QueueScenario(database, PostgresFixture.CreateConnectionFactory(database.Settings));
        }

        public async ValueTask DisposeAsync()
        {
            await _factory.DisposeAsync();
            await Database.DisposeAsync();
        }
    }
}
