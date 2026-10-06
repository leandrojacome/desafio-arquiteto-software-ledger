using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Application.Abstractions;
using Ledger.Domain.Accounts;
using Ledger.Domain.Shared;

namespace Ledger.Api.IntegrationTests.Persistence;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresOutboxTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string TraceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";

    private LedgerHost _host = null!;
    private LedgerQueries _queries = null!;

    public Task InitializeAsync()
    {
        _host = LedgerHost.Create(postgres);
        _queries = new LedgerQueries(postgres);

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
    }

    [DockerFact]
    public async Task EnqueueAsync_StoresTheEnvelopeAndTheJsonPayload()
    {
        var accountId = await _host.CreateAccountAsync();
        var message = Message(accountId, TraceParent);

        await Enqueue(message);

        var row = await _queries.OutboxRowAsync(message.Id);

        row.AccountId.ShouldBe(accountId.Value);
        row.Type.ShouldBe("EntryRegistered");
        row.PayloadEntryId.ShouldBe("0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33");
        row.CorrelationId.ShouldBe(LedgerHost.CorrelationId);
        row.TraceParent.ShouldBe(TraceParent);
    }

    [DockerFact]
    public async Task EnqueueAsync_StoresTheTypeOfTheMessageWhateverItIs()
    {
        var accountId = await _host.CreateAccountAsync();
        var message = Message(accountId, null) with { Type = "EntryRegisteredV2" };

        await Enqueue(message);

        (await _queries.OutboxRowAsync(message.Id)).Type.ShouldBe("EntryRegisteredV2");
    }

    [DockerFact]
    public async Task EnqueueAsync_WithoutATraceparent_StoresNull()
    {
        var accountId = await _host.CreateAccountAsync();
        var message = Message(accountId, null);

        await Enqueue(message);

        (await _queries.OutboxRowAsync(message.Id)).TraceParent.ShouldBeNull();
    }

    [DockerFact]
    public async Task EnqueueAsync_LeavesTheMessageUnpublishedWithTheDatabaseClock()
    {
        var accountId = await _host.CreateAccountAsync();
        var message = Message(accountId, null);
        var before = await _queries.DatabaseNowAsync();

        await Enqueue(message);

        var after = await _queries.DatabaseNowAsync();
        var row = await _queries.OutboxRowAsync(message.Id);

        row.PublishedAt.ShouldBeNull();
        row.Attempts.ShouldBe(0);
        row.CreatedAt.ShouldBeInRange(before, after);
    }

    [DockerFact]
    public async Task EnqueueAsync_WhenTheTransactionIsMarkedForRollback_LeavesNoMessage()
    {
        var accountId = await _host.CreateAccountAsync();
        var message = Message(accountId, null);

        await _host.ExecuteAsync(
            async (scope, token) =>
            {
                await scope.Outbox.EnqueueAsync(message, token);
                scope.MarkForRollback();

                return Result.Success(true);
            });

        (await _queries.CountOutboxAsync(accountId)).ShouldBe(0);
    }

    private static OutboxMessage Message(AccountId accountId, string? traceParent) =>
        new(
            Guid.CreateVersion7(),
            accountId,
            "EntryRegistered",
            "{\"entryId\":\"0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33\",\"accountVersion\":1}",
            LedgerHost.CorrelationId,
            traceParent);

    private async Task Enqueue(OutboxMessage message)
    {
        await _host.InUnitOfWorkAsync(
            async (scope, token) =>
            {
                await scope.Outbox.EnqueueAsync(message, token);

                return true;
            });
    }
}
