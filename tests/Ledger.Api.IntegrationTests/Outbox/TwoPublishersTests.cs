using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Api.IntegrationTests.Outbox;

[Collection(MessagingCollectionDefinition.Name)]
[Trait("Category", "Concurrency")]
public sealed class TwoPublishersTests(PostgresFixture postgres, RabbitMqFixture broker)
{
    private const int MessageCount = 1000;

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(90);

    [DockerFact]
    public async Task TwoWorkersOnTheSameOutbox_PublishEveryMessageExactlyOnceWithoutOverlappingClaims()
    {
        await ConcurrencySettings.RepeatAsync(PublishWithTwoWorkersAsync);
    }

    private async Task PublishWithTwoWorkersAsync()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: true);
        var ids = await scenario.Seeder.InsertPendingAsync(MessageCount);
        var first = new Holder<FaultInjectingOutboxQueue>();
        var second = new Holder<FaultInjectingOutboxQueue>();
        var overrides = new Dictionary<string, string?> { ["Outbox:BatchSize"] = "50" };

        scenario.StartWorker(overrides, Recording(first));
        scenario.StartWorker(overrides, Recording(second));

        var received = await scenario.RequiredProbe.ReceiveAsync(MessageCount, Patience);

        await ConditionWait.UntilAsync(
            async () => await scenario.Seeder.CountPendingAsync() == 0,
            Patience,
            "every message marked as published");

        var extra = await scenario.RequiredProbe.DrainAsync();
        var rows = await scenario.Seeder.ReadRowsAsync();
        var claimedByFirst = Claimed(first);
        var claimedBySecond = Claimed(second);

        received.Count.ShouldBe(MessageCount);
        extra.ShouldBeEmpty();
        received.Select(message => Guid.Parse(message.MessageId ?? string.Empty)).ShouldBe(ids, ignoreOrder: true);
        claimedByFirst.Intersect(claimedBySecond).ShouldBeEmpty();
        (claimedByFirst.Count + claimedBySecond.Count).ShouldBe(MessageCount);
        rows.Sum(row => row.Attempts).ShouldBe(MessageCount);
    }

    private static Action<IServiceCollection> Recording(Holder<FaultInjectingOutboxQueue> holder) =>
        services => services.Decorate<IOutboxQueue>((inner, _) =>
        {
            var queue = new FaultInjectingOutboxQueue(inner);

            holder.Value = queue;

            return queue;
        });

    private static List<Guid> Claimed(Holder<FaultInjectingOutboxQueue> holder) =>
        [.. holder.Value?.Claims.SelectMany(claim => claim) ?? []];

    private sealed class Holder<T>
        where T : class
    {
        public T? Value { get; set; }
    }
}
