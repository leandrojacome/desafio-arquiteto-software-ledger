using System.Diagnostics;
using System.Globalization;
using Ledger.EndToEnd.Tests.Support;
using Xunit.Abstractions;

namespace Ledger.EndToEnd.Tests.Resilience;

[Collection(E2ECollectionDefinition.Name)]
[Trait("Category", "E2E")]
[Trait("Category", "Resilience")]
public sealed class BrokerOutageE2ETests(E2EFixture stack, ITestOutputHelper output)
{
    private const int Entries = 100;

    private const int BaselineEntries = 20;

    private const double FloorMilliseconds = 1000;

    private const double BaselineMultiple = 10;

    private static readonly TimeSpan Recover = TimeSpan.FromSeconds(120);

    private static async Task<LatencySamples> BaselineAsync(E2EApi api)
    {
        var accountId = await api.CreateAccountAsync();
        var samples = new List<TimeSpan>(BaselineEntries);

        for (var index = 0; index < BaselineEntries; index++)
        {
            var response = await api.CreditAsync(accountId, "1.00");

            response.StatusCode.ShouldBe(201, response.Body);
            samples.Add(response.Elapsed);
        }

        return new LatencySamples(samples);
    }

    [E2EFact]
    public async Task WithTheBrokerStopped_WritesKeepGoing_TheOutboxPilesUpAndDrainsWhenItComesBack()
    {
        using var http = stack.CreateClient();
        using var workerHttp = stack.Stack.CreateWorkerClient();
        using var broker = stack.Stack.RabbitMq();
        var api = new E2EApi(http);
        var worker = new E2EApi(workerHttp);
        var database = stack.Stack.Database();
        var compose = stack.Stack.Compose;
        var queue = RabbitMqManagement.NewQueueName("broker-outage");
        var seeded = new List<ReceivedEvent>();

        await broker.DeclareBoundQueueAsync(queue);

        try
        {
            var baseline = await BaselineAsync(api);
            var accountId = await api.FundedAccountAsync("10.00");

            seeded.AddRange(await broker.ConsumeUntilAsync(
                queue,
                events => events.Any(item => item.AccountId == accountId),
                TimeSpan.FromSeconds(30),
                "The funding event did not reach the queue before the outage."));

            var entryIds = new List<string>();
            var slowest = TimeSpan.Zero;
            var ceiling = Math.Max(FloorMilliseconds * LatencySamples.Factor(), BaselineMultiple * baseline.Percentile(0.99));

            try
            {
                await compose.StopAsync(ComposeControl.RabbitMqService);

                for (var index = 0; index < Entries; index++)
                {
                    var response = await api.CreditPacedAsync(accountId, "1.00");

                    response.StatusCode.ShouldBe(201, response.Body);
                    entryIds.Add(response.Text("entryId"));
                    slowest = response.Elapsed > slowest ? response.Elapsed : slowest;
                }

                slowest.TotalMilliseconds.ShouldBeLessThan(
                    ceiling,
                    $"writes must keep their normal time with the broker down (baseline p99 {baseline.Percentile(0.99):0.0} ms)");
                (await api.SendAsync(HttpMethod.Get, "/health/ready", anonymous: true)).StatusCode.ShouldBe(200);
                (await database.PendingOutboxAsync(accountId)).ShouldBe(Entries);

                await E2EWait.UntilAsync(
                    async () =>
                    {
                        var ready = await worker.SendAsync(HttpMethod.Get, "/health/ready", anonymous: true);

                        return ready.StatusCode == 200 && ready.Text("status") == "Degraded";
                    },
                    TimeSpan.FromSeconds(60),
                    "The worker readiness did not become Degraded with the broker down",
                    TimeSpan.FromSeconds(1));
            }
            finally
            {
                await compose.StartAsync(ComposeControl.RabbitMqService);
            }

            var restarted = Stopwatch.GetTimestamp();

            await compose.WaitHealthyAsync(ComposeControl.RabbitMqService, TimeSpan.FromSeconds(90));
            await E2EWait.UntilAsync(
                async () => await database.PendingOutboxAsync(accountId) == 0,
                Recover,
                "The outbox backlog did not drain after the broker came back",
                TimeSpan.FromSeconds(1));
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"The backlog of {Entries} events drained {Stopwatch.GetElapsedTime(restarted).TotalSeconds:0.0} seconds after the broker was started, and writes took at most {slowest.TotalMilliseconds:0} ms while it was down."));

            var after = await broker.ConsumeUntilAsync(
                queue,
                events => events.Count(item => item.AccountId == accountId) >= Entries,
                TimeSpan.FromSeconds(60),
                "The events accumulated during the outage did not reach the queue.");
            var mine = seeded.Concat(after).Where(item => item.AccountId == accountId).ToList();

            mine.Select(item => item.MessageId).Distinct().Count().ShouldBe(Entries + 1);
            mine.Select(item => item.EntryId).Distinct().Count().ShouldBe(Entries + 1);
            entryIds.ShouldAllBe(entryId => mine.Any(item => item.EntryId == entryId));
            (await database.CountEntriesAsync(accountId)).ShouldBe(Entries + 1);
            (await database.OutboxCountAsync(accountId)).ShouldBe(Entries + 1);

            await E2EWait.UntilAsync(
                async () => (await worker.SendAsync(HttpMethod.Get, "/health/ready", anonymous: true)).Text("status") == "Healthy",
                TimeSpan.FromSeconds(90),
                "The worker readiness did not go back to Healthy",
                TimeSpan.FromSeconds(1));
        }
        finally
        {
            await broker.DeleteQueueAsync(queue);
        }
    }
}
