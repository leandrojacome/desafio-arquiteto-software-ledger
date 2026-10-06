using Ledger.EndToEnd.Tests.Support;

namespace Ledger.EndToEnd.Tests.Resilience;

[Collection(E2ECollectionDefinition.Name)]
[Trait("Category", "E2E")]
[Trait("Category", "Resilience")]
public sealed class WorkerRestartE2ETests(E2EFixture stack)
{
    private const int Entries = 40;

    [E2EFact]
    public async Task RestartingTheWorkerInTheMiddleOfTheWrites_PublishesEveryEventAtLeastOnceAndLosesNothing()
    {
        using var http = stack.CreateClient();
        using var workerHttp = stack.Stack.CreateWorkerClient();
        using var broker = stack.Stack.RabbitMq();
        var api = new E2EApi(http);
        var worker = new E2EApi(workerHttp);
        var database = stack.Stack.Database();
        var queue = RabbitMqManagement.NewQueueName("worker-restart");

        await broker.DeclareBoundQueueAsync(queue);

        try
        {
            var accountId = await api.CreateAccountAsync();
            var entryIds = new List<string>();
            Task? restart = null;

            for (var index = 0; index < Entries; index++)
            {
                if (index == Entries / 2)
                {
                    restart = stack.Stack.Compose.RestartAsync(ComposeControl.WorkerService);
                }

                var response = await api.CreditPacedAsync(accountId, "1.00");

                response.StatusCode.ShouldBe(201, response.Body);
                entryIds.Add(response.Text("entryId"));
                await Task.Delay(TimeSpan.FromMilliseconds(60), CancellationToken.None);
            }

            await restart.ShouldNotBeNull();
            await E2EWait.UntilAsync(
                async () => await worker.ProbeStatusAsync("/health/ready") == 200,
                TimeSpan.FromSeconds(60),
                "The worker did not become ready after the restart",
                TimeSpan.FromSeconds(1));
            await E2EWait.UntilAsync(
                async () => await database.PendingOutboxAsync(accountId) == 0,
                TimeSpan.FromSeconds(60),
                "The outbox backlog did not drain after the worker restart",
                TimeSpan.FromSeconds(1));

            var received = await broker.ConsumeUntilAsync(
                queue,
                events => events.Count(item => item.AccountId == accountId) >= Entries,
                TimeSpan.FromSeconds(60),
                "Not every event reached the queue after the worker restart.");
            await Task.Delay(TimeSpan.FromSeconds(3), CancellationToken.None);
            var mine = received.Concat(await broker.ConsumeAllAsync(queue)).Where(item => item.AccountId == accountId).ToList();

            mine.Select(item => item.EntryId).Distinct().Order().ShouldBe(entryIds.Order());
            mine.Select(item => item.MessageId).Distinct().Count().ShouldBe(Entries, "every entry must have exactly one event identity, even when delivery repeats");
            mine.GroupBy(item => item.MessageId).ShouldAllBe(
                group => group.Select(item => item.EntryId).Distinct().Count() == 1,
                "a repeated delivery must carry the same entry");
            (await database.CountEntriesAsync(accountId)).ShouldBe(Entries);
            (await database.InvariantViolationsAsync(accountId)).ShouldBeEmpty();
        }
        finally
        {
            await broker.DeleteQueueAsync(queue);
        }
    }
}
