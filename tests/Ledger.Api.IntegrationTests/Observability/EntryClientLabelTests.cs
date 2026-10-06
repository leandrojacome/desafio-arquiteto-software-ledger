using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Writes.Support;

namespace Ledger.Api.IntegrationTests.Observability;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class EntryClientLabelTests(PostgresFixture postgres)
{
    private const string MeterName = "Ledger";
    private const string RecordedEntries = "ledger.entries.recorded";
    private const string ClientTag = "client";

    [DockerFact]
    public async Task AnEntryRecordedThroughTheApi_IsCountedUnderTheClientIdOfTheToken()
    {
        using var counter = new TaggedCounter(MeterName, RecordedEntries);
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var account = await client.CreateAccountAsync();
        var options = new WriteRequestOptions { Token = TestTokenFactory.Create(clientId: "label-client-a") };

        var response = await client.PostEntryAsync(account, WriteClient.EntryBody("CREDIT", "10.00"), WriteClient.NewKey(), options);

        response.StatusCode.ShouldBe(201, response.Body);
        counter.TotalWhere(ClientTag, "label-client-a").ShouldBe(1);
        counter.Total.ShouldBe(1);
    }

    [DockerFact]
    public async Task EntriesOfTwoClients_AreCountedSeparately()
    {
        using var counter = new TaggedCounter(MeterName, RecordedEntries);
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var account = await client.CreateAccountAsync();
        var first = new WriteRequestOptions { Token = TestTokenFactory.Create(clientId: "label-client-b") };
        var second = new WriteRequestOptions { Token = TestTokenFactory.Create(clientId: "label-client-c") };

        (await client.PostEntryAsync(account, WriteClient.EntryBody("CREDIT", "10.00"), WriteClient.NewKey(), first)).StatusCode.ShouldBe(201);
        (await client.PostEntryAsync(account, WriteClient.EntryBody("CREDIT", "5.00"), WriteClient.NewKey(), second)).StatusCode.ShouldBe(201);
        (await client.PostEntryAsync(account, WriteClient.EntryBody("CREDIT", "1.00"), WriteClient.NewKey(), second)).StatusCode.ShouldBe(201);

        counter.TotalWhere(ClientTag, "label-client-b").ShouldBe(1);
        counter.TotalWhere(ClientTag, "label-client-c").ShouldBe(2);
    }

    [DockerFact]
    public async Task AReversalThroughTheApi_IsCountedUnderTheClientIdOfTheToken()
    {
        using var counter = new TaggedCounter(MeterName, RecordedEntries);
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var account = await client.CreateAccountAsync();
        var options = new WriteRequestOptions { Token = TestTokenFactory.Create(clientId: "label-client-d") };
        var credit = await client.PostEntryAsync(account, WriteClient.EntryBody("CREDIT", "10.00"), WriteClient.NewKey(), options);

        var reversal = await client.PostReversalAsync(account, credit.Text("entryId"), WriteClient.NewKey(), options: options);

        reversal.StatusCode.ShouldBe(201, reversal.Body);
        counter.TotalWhere(ClientTag, "label-client-d").ShouldBe(2);
    }
}
