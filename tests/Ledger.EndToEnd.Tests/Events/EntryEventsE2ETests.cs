using Ledger.EndToEnd.Tests.Support;

namespace Ledger.EndToEnd.Tests.Events;

[Collection(E2ECollectionDefinition.Name)]
[Trait("Category", "E2E")]
public sealed class EntryEventsE2ETests(E2EFixture stack)
{
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(30);

    [E2EFact]
    public async Task EachEntry_ReachesTheReferenceConsumerOnce_WithTheCorrelationIdAndNoDocument()
    {
        using var http = stack.CreateClient();
        using var broker = stack.Stack.RabbitMq();
        var api = new E2EApi(http);
        var queue = RabbitMqManagement.NewQueueName("entry-registered");
        var document = E2EApi.NewCpf();
        const string Correlation = "e2e-event-correlation-0001";

        await broker.DeclareBoundQueueAsync(queue);

        try
        {
            var created = await api.OpenAccountAsync(document);
            var accountId = created.Text("accountId");
            var credit = await api.SendAsync(
                HttpMethod.Post,
                $"/v1/accounts/{accountId}/entries",
                """{"type":"CREDIT","amount":"200.00","currency":"BRL"}""",
                E2EApi.NewKey(),
                headers: new Dictionary<string, string> { ["X-Correlation-Id"] = Correlation });
            var debit = await api.DebitAsync(accountId, "80.00");
            var reversal = await api.ReverseAsync(accountId, debit.Text("entryId"));

            var received = await broker.ConsumeUntilAsync(
                queue,
                events => events.Count(item => item.AccountId == accountId) >= 3,
                DeliveryTimeout,
                "The three events of the account did not reach the test queue.");
            await Task.Delay(TimeSpan.FromSeconds(3), CancellationToken.None);
            var late = await broker.ConsumeAllAsync(queue);

            var mine = received.Concat(late).Where(item => item.AccountId == accountId).ToList();

            mine.Count.ShouldBe(3, "an event was delivered more than once or went missing");
            mine.Select(item => item.MessageId).Distinct().Count().ShouldBe(3);
            mine.ShouldAllBe(item => item.RoutingKey == RabbitMqManagement.EntryRegisteredKey);
            mine.Select(item => item.EntryId).Order().ShouldBe(
                new[] { credit.Text("entryId"), debit.Text("entryId"), reversal.Text("entryId") }.Order());

            var creditEvent = mine.Single(item => item.EntryId == credit.Text("entryId"));
            var reversalEvent = mine.Single(item => item.EntryId == reversal.Text("entryId"));

            creditEvent.CorrelationId.ShouldBe(Correlation);
            creditEvent.Payload.GetProperty("eventType").GetString().ShouldBe("EntryRegistered");
            creditEvent.Payload.GetProperty("eventId").GetString().ShouldBe(creditEvent.MessageId);
            creditEvent.Payload.GetProperty("type").GetString().ShouldBe("CREDIT");
            creditEvent.Payload.GetProperty("amount").GetString().ShouldBe("200.00");
            creditEvent.Payload.GetProperty("balanceAfter").GetString().ShouldBe("200.00");
            reversalEvent.Payload.GetProperty("reversesEntryId").GetString().ShouldBe(debit.Text("entryId"));
            reversalEvent.Payload.GetProperty("balanceAfter").GetString().ShouldBe("200.00");
            mine.ShouldAllBe(item => !item.Payload.GetRawText().Contains(document, StringComparison.Ordinal));
        }
        finally
        {
            await broker.DeleteQueueAsync(queue);
        }
    }

    [E2EFact]
    public async Task ARefusedWrite_PublishesNoEvent()
    {
        using var http = stack.CreateClient();
        using var broker = stack.Stack.RabbitMq();
        var api = new E2EApi(http);
        var queue = RabbitMqManagement.NewQueueName("refused");
        var accountId = await api.CreateAccountAsync();

        await broker.DeclareBoundQueueAsync(queue);

        try
        {
            var refused = await api.DebitAsync(accountId, "10.00");
            var accepted = await api.CreditAsync(accountId, "5.00");

            var received = await broker.ConsumeUntilAsync(
                queue,
                events => events.Any(item => item.AccountId == accountId),
                DeliveryTimeout,
                "The event of the accepted credit did not reach the test queue.");
            await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);
            var mine = received.Concat(await broker.ConsumeAllAsync(queue)).Where(item => item.AccountId == accountId).ToList();

            refused.StatusCode.ShouldBe(422, refused.Body);
            accepted.StatusCode.ShouldBe(201, accepted.Body);
            mine.Select(item => item.EntryId).ShouldBe([accepted.Text("entryId")]);
        }
        finally
        {
            await broker.DeleteQueueAsync(queue);
        }
    }
}
