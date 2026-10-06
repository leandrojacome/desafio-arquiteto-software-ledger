using System.Text.Json;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Writes.Support;

namespace Ledger.Api.IntegrationTests.Writes.Entries;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class EntryOutboxTests(PostgresFixture postgres)
{
    private const string TraceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";

    private readonly WriteTestData _data = new(postgres);

    private async Task<(Guid Id, string Type, string CorrelationId, string? TraceParent, DateTime? PublishedAt, string Payload)>
        OutboxRowAsync(string accountId)
    {
        await using var command = postgres.AdministrativeSource.CreateCommand(
            "SELECT id, type, correlation_id, traceparent, published_at, payload::text FROM outbox_messages WHERE account_id = @account_id");

        command.Parameters.AddWithValue("account_id", WriteTestData.Account(accountId).Value);

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        (await reader.ReadAsync(CancellationToken.None)).ShouldBeTrue();

        var row = (
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            await reader.IsDBNullAsync(3, CancellationToken.None) ? null : reader.GetString(3),
            await reader.IsDBNullAsync(4, CancellationToken.None) ? (DateTime?)null : reader.GetDateTime(4),
            reader.GetString(5));

        (await reader.ReadAsync(CancellationToken.None)).ShouldBeFalse();

        return row;
    }

    [DockerFact]
    public async Task AcceptedEntry_LeavesOneUnpublishedEventWhoseFieldsAreThoseOfTheResponse()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();
        const string correlation = "9f3c1a7e2b4d4f60a1c8e5d7b3a29f10";

        var response = await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("CREDIT", "80.00", description: "free text", reference: "ref-123"),
            WriteClient.NewKey(),
            new WriteRequestOptions { CorrelationId = correlation });

        response.StatusCode.ShouldBe(201, response.Body);

        var row = await OutboxRowAsync(accountId);

        row.Type.ShouldBe("EntryRegistered");
        row.CorrelationId.ShouldBe(correlation);
        row.PublishedAt.ShouldBeNull();

        using var payload = JsonDocument.Parse(row.Payload);
        var root = payload.RootElement;

        root.GetProperty("eventId").GetString().ShouldBe(row.Id.ToString("D"));
        root.GetProperty("eventType").GetString().ShouldBe("EntryRegistered");
        root.GetProperty("schemaVersion").GetInt32().ShouldBe(1);
        root.GetProperty("accountId").GetString().ShouldBe(accountId);
        root.GetProperty("entryId").GetString().ShouldBe(response.Text("entryId"));
        root.GetProperty("accountVersion").GetInt64().ShouldBe(1);
        root.GetProperty("type").GetString().ShouldBe("CREDIT");
        root.GetProperty("amount").GetString().ShouldBe("80.00");
        root.GetProperty("currency").GetString().ShouldBe("BRL");
        root.GetProperty("balanceAfter").GetString().ShouldBe(response.Text("balanceAfter"));
        root.GetProperty("recordedAt").GetString().ShouldBe(response.Text("recordedAt"));
        root.GetProperty("occurredAt").GetString().ShouldBe(response.Text("occurredAt"));
        root.GetProperty("reversesEntryId").ValueKind.ShouldBe(JsonValueKind.Null);
        root.GetProperty("correlationId").GetString().ShouldBe(correlation);

        var names = root.EnumerateObject().Select(property => property.Name).ToList();

        names.ShouldNotContain("description");
        names.ShouldNotContain("reference");
        names.ShouldNotContain("clientId");
        row.Payload.ShouldNotContain("free text");
        row.Payload.ShouldNotContain("ref-123");
    }

    [DockerFact]
    public async Task EventOfAnEntryWithTheTraceContext_CarriesTheW3cTraceParent()
    {
        await using var factory = new WriteApiFactory(postgres);
        var http = factory.CreateClient();
        var client = new WriteClient(http);
        var accountId = await client.CreateAccountAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/accounts/{accountId}/entries");

        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {TestTokenFactory.Create()}");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", WriteClient.NewKey());
        request.Headers.TryAddWithoutValidation("traceparent", TraceParent);
        request.Content = new StringContent(WriteClient.EntryBody("CREDIT", "1.00"), System.Text.Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(request, CancellationToken.None);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.Created);

        var row = await OutboxRowAsync(accountId);

        row.TraceParent.ShouldNotBeNull();
        row.TraceParent.Length.ShouldBeLessThanOrEqualTo(55);
        row.TraceParent.ShouldStartWith("00-");
        row.TraceParent.ShouldContain("4bf92f3577b34da6a3ce929d0e0e4736");
    }

    [DockerFact]
    public async Task ReplayRefusalAndConflict_WriteNoEvent()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("100.00");
        var key = WriteClient.NewKey();

        await client.DebitAsync(accountId, "10.00", key);
        await client.DebitAsync(accountId, "10.00", key);
        await client.DebitAsync(accountId, "11.00", key);
        await client.DebitAsync(accountId, "10000.00");
        await client.PostEntryAsync(accountId, WriteClient.EntryBody("CREDIT", "1.00", "EUR"), WriteClient.NewKey());

        (await _data.CountOutboxAsync(accountId)).ShouldBe(2);
        (await _data.CountEntriesAsync(accountId)).ShouldBe(2);
    }

    [DockerFact]
    public async Task BrokerNotReachable_DoesNotSlowOrFailTheWritesAndTheEventsStayUnpublished()
    {
        await using var factory = new WriteApiFactory(postgres, WriteApiFactory.WidePool);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();

        var responses = await ParallelGate.RunAsync(100, _ => client.CreditAsync(accountId, "1.00"));

        responses.ShouldAllBe(response => response.StatusCode == 201);
        (await _data.CountOutboxAsync(accountId)).ShouldBe(100);

        using var ready = await factory.CreateClient().GetAsync("/health/ready", CancellationToken.None);

        ready.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
    }
}
