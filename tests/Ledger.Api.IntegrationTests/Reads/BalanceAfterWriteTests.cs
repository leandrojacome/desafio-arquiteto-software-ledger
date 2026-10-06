using System.Net;
using Ledger.Api.IntegrationTests.Infrastructure;

namespace Ledger.Api.IntegrationTests.Reads;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class BalanceAfterWriteTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const int Pairs = 1000;

    private ReadWorld _writerInstance = null!;
    private ReadWriteClient _writer = null!;
    private ReadApiFactory _readerInstance = null!;
    private ReadApiClient _reader = null!;

    public Task InitializeAsync()
    {
        _writerInstance = ReadWorld.Create(postgres);
        _writer = ReadWriteClient.For(_writerInstance.Factory);
        _readerInstance = ReadApiFactory.WithDatabase(postgres);
        _reader = ReadApiClient.For(_readerInstance);

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _reader.Dispose();
        await _readerInstance.DisposeAsync();
        _writer.Dispose();
        await _writerInstance.DisposeAsync();
    }

    [DockerFact]
    public async Task Reads_OnAnotherInstanceRightAfterEachWrite_SeeTheEntryAndTheNewBalance()
    {
        var accountId = await _writerInstance.Host.CreateFundedAccountAsync(1000.00m);

        for (var step = 1; step <= Pairs; step++)
        {
            var type = step % 4 == 0 ? "DEBIT" : "CREDIT";

            using var written = await _writer.PostEntryAsync(accountId, $"after-write-{step}", type, "3.25");

            written.Status.ShouldBe(HttpStatusCode.Created, written.Body);

            var entryId = written.Text("entryId");
            var balanceAfter = written.Money("balanceAfter");

            using var balance = await _reader.BalanceAsync(accountId);
            using var atTheEntry = await _reader.BalanceAsOfAsync(accountId, written.Text("recordedAt"));
            using var top = await _reader.StatementAsync(accountId, "limit=1");

            balance.Status.ShouldBe(HttpStatusCode.OK, balance.Body);
            balance.Money("balance").ShouldBe(balanceAfter);
            balance.Text("lastEntryId").ShouldBe(entryId);
            atTheEntry.Money("balance").ShouldBe(balanceAfter);
            atTheEntry.Text("lastEntryId").ShouldBe(entryId);
            StatementItem.ItemsOf(top).ShouldHaveSingleItem().EntryId.ShouldBe(entryId);
        }
    }

    [DockerFact]
    public async Task Reads_AfterAnIdempotentReplayAndAnInsufficientFundsRefusal_DoNotChange()
    {
        var accountId = await _writerInstance.Host.CreateFundedAccountAsync(50.00m);

        using var original = await _writer.PostEntryAsync(accountId, "replayed", "DEBIT", "20.00");

        using var beforeReplay = await _reader.BalanceAsync(accountId);

        using var replay = await _writer.PostEntryAsync(accountId, "replayed", "DEBIT", "20.00");

        using var afterReplay = await _reader.BalanceAsync(accountId);

        using var refused = await _writer.PostEntryAsync(accountId, "too-much", "DEBIT", "500.00");

        using var afterRefusal = await _reader.BalanceAsync(accountId);
        using var statement = await _reader.StatementAsync(accountId);

        original.Status.ShouldBe(HttpStatusCode.Created, original.Body);
        original.HasHeader("Idempotent-Replayed").ShouldBeFalse();
        replay.Status.ShouldBe(HttpStatusCode.Created, replay.Body);
        replay.Header("Idempotent-Replayed").ShouldBe("true");
        replay.Text("entryId").ShouldBe(original.Text("entryId"));
        refused.Status.ShouldBe(HttpStatusCode.UnprocessableEntity, refused.Body);
        refused.Text("code").ShouldBe("INSUFFICIENT_FUNDS");

        foreach (var response in new[] { beforeReplay, afterReplay, afterRefusal })
        {
            response.Money("balance").ShouldBe(30.00m);
            response.Text("lastEntryId").ShouldBe(original.Text("entryId"));
        }

        StatementItem.ItemsOf(statement).Count.ShouldBe(2);
    }
}
