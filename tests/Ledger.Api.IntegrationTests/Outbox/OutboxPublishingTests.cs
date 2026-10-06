using System.Text.Json;
using Ledger.Api.IntegrationTests.Infrastructure;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Ledger.Api.IntegrationTests.Outbox;

[Collection(MessagingCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class OutboxPublishingTests(PostgresFixture postgres, RabbitMqFixture broker)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    [DockerFact]
    public async Task Worker_WithAThousandPendingMessages_PublishesEachOneOnceAndMarksThemPublished()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: true);
        using var published = new MeterCounter("Ledger", "outbox.published");
        var ids = await scenario.Seeder.InsertPendingAsync(1000);

        scenario.StartWorker();

        var received = await scenario.RequiredProbe.ReceiveAsync(ids.Count, Patience);

        await ConditionWait.UntilAsync(
            async () => await scenario.Seeder.CountPendingAsync() == 0,
            Patience,
            "every message marked as published");

        var rows = await scenario.Seeder.ReadRowsAsync();

        published.Total.ShouldBe(1000);
        received.Count.ShouldBe(1000);
        received.Select(message => message.MessageId).Distinct().Count().ShouldBe(1000);
        received.Select(message => Guid.Parse(message.MessageId ?? string.Empty)).ToHashSet().ShouldBe(ids.ToHashSet());
        rows.ShouldAllBe(row => row.PublishedAt != null && row.LockedUntil == null && row.Attempts == 1);
    }

    [DockerFact]
    public async Task Worker_PublishedMessage_CarriesTheContractEnvelope()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: true);
        var accountId = Guid.NewGuid();
        var ids = await scenario.Seeder.InsertPendingAsync(1, accountId, withTraceParent: true);

        scenario.StartWorker();

        var message = (await scenario.RequiredProbe.ReceiveAsync(1, Patience)).ShouldHaveSingleItem();
        var row = (await scenario.Seeder.ReadRowsAsync()).ShouldHaveSingleItem();

        message.MessageId.ShouldBe(ids[0].ToString("D"));
        message.Type.ShouldBe("EntryRegistered");
        message.RoutingKey.ShouldBe("EntryRegistered");
        message.DeliveryMode.ShouldBe(DeliveryModes.Persistent);
        message.ContentType.ShouldBe("application/json");
        message.ContentEncoding.ShouldBe("utf-8");
        message.Timestamp.ShouldBe(row.CreatedAt.ToUnixTimeSeconds());
        message.Headers["account-id"].ShouldBe(accountId.ToString("D"));
        message.Headers["schema-version"].ShouldBe(1);
        message.Headers["traceparent"].ShouldBe("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");

        using var body = JsonDocument.Parse(message.Body);

        body.RootElement.GetProperty("eventId").GetString().ShouldBe(message.MessageId);
        message.CorrelationId.ShouldBe(body.RootElement.GetProperty("correlationId").GetString());
    }

    [DockerFact]
    public async Task Worker_MessageWithoutTraceParent_CarriesNoTraceHeader()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: true);
        await scenario.Seeder.InsertPendingAsync(1, withTraceParent: false);

        scenario.StartWorker();

        var message = (await scenario.RequiredProbe.ReceiveAsync(1, Patience)).ShouldHaveSingleItem();

        message.Headers.ShouldNotContainKey("traceparent");
    }

    [DockerFact]
    public async Task Worker_PublishedBody_IsTheTextOfTheJsonbColumn()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: true);
        await scenario.Seeder.InsertPendingAsync(1);

        scenario.StartWorker();

        var message = (await scenario.RequiredProbe.ReceiveAsync(1, Patience)).ShouldHaveSingleItem();
        var row = (await scenario.Seeder.ReadRowsAsync()).ShouldHaveSingleItem();

        message.Body.ShouldBe(row.Payload);
        message.Body.ShouldContain("\"amount\": \"10.00\"");
        message.Body.ShouldContain("\"reversesEntryId\": null");
    }

    [DockerFact]
    public async Task Worker_DeclaresADurableTopicExchangeOnConnect()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);

        scenario.StartWorker();

        await ConditionWait.UntilAsync(
            async () => await ExchangeExistsAsync(),
            Patience,
            "the ledger.events exchange to be declared");

        await using var connection = await broker.CreateConnectionAsync(CancellationToken.None);
        await using var channel = await connection.CreateChannelAsync();

        await channel.ExchangeDeclareAsync(
            RabbitMqFixture.Exchange,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            noWait: false,
            CancellationToken.None);

        var mismatch = await Should.ThrowAsync<OperationInterruptedException>(async () =>
        {
            await using var other = await connection.CreateChannelAsync();

            await other.ExchangeDeclareAsync(
                RabbitMqFixture.Exchange,
                ExchangeType.Direct,
                durable: true,
                autoDelete: false,
                arguments: null,
                noWait: false,
                CancellationToken.None);
        });

        mismatch.ShutdownReason.ShouldNotBeNull().ReplyCode.ShouldBe((ushort)406);
    }

    private async Task<bool> ExchangeExistsAsync()
    {
        try
        {
            await using var connection = await broker.CreateConnectionAsync(CancellationToken.None);
            await using var channel = await connection.CreateChannelAsync();

            await channel.ExchangeDeclarePassiveAsync(RabbitMqFixture.Exchange, CancellationToken.None);

            return true;
        }
        catch (OperationInterruptedException)
        {
            return false;
        }
    }
}
