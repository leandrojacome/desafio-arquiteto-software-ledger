using System.Text;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Observability;
using Ledger.Api.IntegrationTests.Writes.Support;
using Ledger.Application.Abstractions;

namespace Ledger.Api.IntegrationTests.Writes.Entries;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Security")]
[Trait("Category", "Integration")]
public sealed class WriteLogLeakTests(PostgresFixture postgres)
{
    private const string Document = "123.456.789-09";
    private const string DescriptionCanary = "DESCRIPTION-CANARY-5b1d";
    private const string ReferenceCanary = "REFERENCE-CANARY-9a7e";
    private const string KeyCanary = "KEY-CANARY-c3f0-0001";
    private const string UnknownFieldCanary = "UNKNOWNCANARY";
    private const string BadValueCanary = "BADVALUE-CANARY-77e2";

    private readonly WriteTestData _data = new(postgres);

    [DockerFact]
    public async Task AJourneyOfAcceptedRefusedAndRejectedCalls_LeavesNoSensitiveValueInLogsResponsesOrStoredTables()
    {
        using var clean = OtelEnvironment.Clean();
        var sink = new CapturingLogSink();
        var outage = new OutageSwitch();
        await using var factory = new WriteApiFactory(
            postgres,
            WriteApiFactory.WidePool,
            configureServices: services => services.Decorate<IHolderDocumentProtector>(
                (inner, _) => new SwitchedProtector(inner, outage)),
            logSink: sink);
        var client = new WriteClient(factory.CreateClient());
        var bearer = TestTokenFactory.Create();
        var responses = new List<ApiResponse>();

        var created = await client.PostAccountAsync($"{{\"holderDocument\":\"{Document}\",\"currency\":\"BRL\"}}");
        var accountId = created.Text("accountId");
        responses.Add(created);

        var entry = await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("CREDIT", "500.00", description: DescriptionCanary, reference: ReferenceCanary),
            KeyCanary);
        responses.Add(entry);
        responses.Add(await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("CREDIT", "500.00", description: DescriptionCanary, reference: ReferenceCanary),
            KeyCanary));
        responses.Add(await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("CREDIT", "501.00", description: DescriptionCanary, reference: ReferenceCanary),
            KeyCanary));
        responses.Add(await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("DEBIT", "99999.00", description: DescriptionCanary, reference: ReferenceCanary),
            $"{KeyCanary}-2"));
        responses.Add(await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("DEBIT", "1.00", "ZZZ", description: DescriptionCanary),
            $"{KeyCanary}-3"));
        responses.Add(await client.PostEntryAsync(
            accountId,
            $"{{\"type\":\"{BadValueCanary}\",\"amount\":\"{BadValueCanary}\",\"currency\":\"{BadValueCanary}\"," +
            $"\"{UnknownFieldCanary}\":\"{BadValueCanary}\",\"description\":\"{DescriptionCanary}\"}}",
            $"{KeyCanary}-4"));
        responses.Add(await client.PostReversalAsync(
            accountId,
            entry.Text("entryId"),
            $"{KeyCanary}-5",
            $"{{\"description\":\"{DescriptionCanary}\"}}"));
        responses.Add(await client.PostReversalAsync(
            accountId,
            entry.Text("entryId"),
            $"{KeyCanary}-6",
            $"{{\"description\":\"{DescriptionCanary}\"}}"));
        responses.Add(await client.PostAccountAsync(
            $"{{\"holderDocument\":\"{Document}\",\"currency\":\"{BadValueCanary}\"}}"));
        responses.Add(await client.PostAccountAsync(null, new WriteRequestOptions { Anonymous = true }));
        responses.Add(await client.PostAccountAsync(
            null,
            new WriteRequestOptions { Token = TestTokenFactory.Create(scope: "ledger.read") }));

        outage.Available = false;
        responses.Add(await client.PostAccountAsync($"{{\"holderDocument\":\"{Document}\",\"currency\":\"BRL\"}}"));
        outage.Available = true;

        var stored = await _data.StoredAccountAsync(accountId);
        var everything = sink.Everything();
        var forbidden = new List<string>
        {
            Document,
            "12345678909",
            "456.789",
            DescriptionCanary,
            ReferenceCanary,
            KeyCanary,
            BadValueCanary,
            UnknownFieldCanary,
            Convert.ToHexStringLower(stored.Encrypted),
            Convert.ToHexString(stored.Encrypted),
            Convert.ToBase64String(stored.Encrypted),
            Convert.ToBase64String(stored.BlindIndex),
            Convert.ToHexStringLower(stored.BlindIndex),
            bearer
        };

        sink.Events.ShouldNotBeEmpty();

        var storedText = await _data.EverythingStoredAsTextAsync();

        storedText.ShouldNotContain("12345678909");
        storedText.ShouldNotContain(Document);

        foreach (var value in forbidden)
        {
            everything.ShouldNotContain(value, Case.Sensitive);
        }

        foreach (var response in responses.Where(candidate => candidate.StatusCode >= 400))
        {
            response.Body.ShouldNotContain(Document);
            response.Body.ShouldNotContain(DescriptionCanary);
            response.Body.ShouldNotContain(ReferenceCanary);
            response.Body.ShouldNotContain(KeyCanary);
            response.Body.ShouldNotContain(BadValueCanary);
        }

        responses.Select(response => response.StatusCode).ShouldBe([201, 201, 201, 422, 422, 422, 400, 201, 409, 400, 401, 403, 503]);
        Encoding.UTF8.GetByteCount(everything).ShouldBeGreaterThan(0);
    }
}
