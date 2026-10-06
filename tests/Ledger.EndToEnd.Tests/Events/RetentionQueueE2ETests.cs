using Ledger.EndToEnd.Tests.Support;

namespace Ledger.EndToEnd.Tests.Events;

[Collection(E2ECollectionDefinition.Name)]
[Trait("Category", "E2E")]
public sealed class RetentionQueueE2ETests(E2EFixture stack)
{
    private static readonly TimeSpan DeclarationTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(30);

    [E2EFact]
    public async Task TheRetentionQueue_IsDeclaredByTheWorkerAsADurableQuorumQueueBoundToTheEventType()
    {
        using var broker = stack.Stack.RabbitMq();

        await WaitForTheQueueAsync(broker, RabbitMqManagement.RetentionQueue);

        var queue = (await broker.QueueAsync(RabbitMqManagement.RetentionQueue)).ShouldNotBeNull();
        var arguments = queue.GetProperty("arguments");
        var bindings = await broker.BindingsAsync(RabbitMqManagement.RetentionQueue);

        queue.GetProperty("durable").GetBoolean().ShouldBeTrue();
        queue.GetProperty("type").GetString().ShouldBe("quorum");
        arguments.GetProperty("x-message-ttl").GetInt64().ShouldBe(86_400_000L);
        arguments.GetProperty("x-max-length").GetInt64().ShouldBe(1_000_000L);
        arguments.GetProperty("x-max-length-bytes").GetInt64().ShouldBe(1_073_741_824L);
        arguments.GetProperty("x-overflow").GetString().ShouldBe("drop-head");
        arguments.GetProperty("x-dead-letter-exchange").GetString().ShouldBe(RabbitMqManagement.RetentionQueue + ".dlx");
        arguments.GetProperty("x-dead-letter-routing-key").GetString().ShouldBe("dead");
        bindings.ShouldContain((RabbitMqManagement.Exchange, RabbitMqManagement.EntryRegisteredKey));

        var deadLetter = (await broker.QueueAsync(RabbitMqManagement.RetentionDeadLetterQueue)).ShouldNotBeNull();

        deadLetter.GetProperty("durable").GetBoolean().ShouldBeTrue();
        deadLetter.GetProperty("arguments").GetProperty("x-message-ttl").GetInt64().ShouldBe(604_800_000L);
        deadLetter.GetProperty("arguments").GetProperty("x-max-length").GetInt64().ShouldBe(100_000L);
    }

    [E2EFact]
    public async Task EveryEntryWrittenThroughTheApi_IsWaitingInTheRetentionQueueExactlyOnce()
    {
        using var http = stack.CreateClient();
        using var broker = stack.Stack.RabbitMq();
        var api = new E2EApi(http);

        await WaitForTheQueueAsync(broker, RabbitMqManagement.RetentionQueue);
        await broker.ConsumeAllAsync(RabbitMqManagement.RetentionQueue);

        var accountId = await api.CreateAccountAsync();
        var responses = new[]
        {
            await api.CreditAsync(accountId, "100.00"),
            await api.DebitAsync(accountId, "10.00"),
            await api.DebitAsync(accountId, "20.00"),
            await api.CreditAsync(accountId, "5.00"),
            await api.DebitAsync(accountId, "1.00")
        };

        responses.ShouldAllBe(response => response.StatusCode == 201);

        var received = await broker.ConsumeUntilAsync(
            RabbitMqManagement.RetentionQueue,
            events => events.Count(item => item.AccountId == accountId) >= responses.Length,
            DeliveryTimeout,
            "The entries written through the API did not reach the retention queue.");
        await Task.Delay(TimeSpan.FromSeconds(3), CancellationToken.None);
        var late = await broker.ConsumeAllAsync(RabbitMqManagement.RetentionQueue);

        var mine = received.Concat(late).Where(item => item.AccountId == accountId).ToList();

        mine.Count.ShouldBe(responses.Length, "an event was retained more than once or went missing");
        mine.Select(item => item.MessageId).Distinct().Count().ShouldBe(responses.Length);
        mine.ShouldAllBe(item => item.RoutingKey == RabbitMqManagement.EntryRegisteredKey);
        mine.Select(item => item.EntryId).Order().ShouldBe(responses.Select(response => response.Text("entryId")).Order());
    }

    private static Task WaitForTheQueueAsync(RabbitMqManagement broker, string queue) =>
        E2EWait.UntilAsync(
            async () => await broker.QueueAsync(queue) is not null,
            DeclarationTimeout,
            $"The worker did not declare the queue {queue}.",
            TimeSpan.FromSeconds(1));
}
