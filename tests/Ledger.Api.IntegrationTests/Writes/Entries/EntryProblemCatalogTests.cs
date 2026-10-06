using Ledger.Api.ErrorHandling;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Writes.Support;

namespace Ledger.Api.IntegrationTests.Writes.Entries;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class EntryProblemCatalogTests(PostgresFixture postgres)
{
    [DockerFact]
    public async Task EveryCodeOfTheWritePath_ComesOutWithItsStatusTitleAndStableShape()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("100.00");
        var original = await client.DebitAsync(accountId, "10.00");
        var reversal = await client.PostReversalAsync(accountId, original.Text("entryId"), WriteClient.NewKey());
        var keyOfReuse = WriteClient.NewKey();
        await client.DebitAsync(accountId, "1.00", keyOfReuse);
        var readOnly = TestTokenFactory.Create(scope: "ledger.read");
        var unknown = Guid.NewGuid().ToString("D");

        var cases = new (int Status, string Code, ApiResponse Response)[]
        {
            (400, "VALIDATION_FAILED", await client.PostEntryAsync(accountId, "{}", WriteClient.NewKey())),
            (400, "IDEMPOTENCY_KEY_REQUIRED", await client.PostEntryAsync(accountId, WriteClient.EntryBody("CREDIT", "1.00"), null)),
            (401, "UNAUTHENTICATED", await client.PostEntryAsync(accountId, "{}", null, new WriteRequestOptions { Anonymous = true })),
            (403, "FORBIDDEN", await client.PostEntryAsync(accountId, "{}", null, new WriteRequestOptions { Token = readOnly })),
            (404, "ACCOUNT_NOT_FOUND", await client.CreditAsync(unknown, "1.00")),
            (404, "ENTRY_NOT_FOUND", await client.PostReversalAsync(accountId, unknown, WriteClient.NewKey())),
            (405, "METHOD_NOT_ALLOWED", await client.SendAsync(HttpMethod.Put, $"/v1/accounts/{accountId}/entries", "{}")),
            (409, "ENTRY_ALREADY_REVERSED", await client.PostReversalAsync(accountId, original.Text("entryId"), WriteClient.NewKey())),
            (413, "PAYLOAD_TOO_LARGE", await client.PostEntryAsync(accountId, new string('x', 20_000), WriteClient.NewKey())),
            (415, "UNSUPPORTED_MEDIA_TYPE", await client.PostEntryAsync(accountId, "x", WriteClient.NewKey(), new WriteRequestOptions { ContentType = "text/plain" })),
            (422, "INSUFFICIENT_FUNDS", await client.DebitAsync(accountId, "100000.00")),
            (422, "CURRENCY_MISMATCH", await client.PostEntryAsync(accountId, WriteClient.EntryBody("CREDIT", "1.00", "EUR"), WriteClient.NewKey())),
            (422, "IDEMPOTENCY_KEY_REUSED", await client.DebitAsync(accountId, "2.00", keyOfReuse)),
            (422, "ENTRY_NOT_REVERSIBLE", await client.PostReversalAsync(accountId, reversal.Text("entryId"), WriteClient.NewKey()))
        };

        foreach (var (status, code, response) in cases)
        {
            var problem = response.ShouldBeProblem(status, code);

            problem.GetProperty("title").GetString().ShouldBe(ProblemCatalog.TitleFor(code), code);
            problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
            response.Header("Cache-Control").ShouldBe("no-store", code);
            response.Header("X-Correlation-Id").ShouldBe(problem.GetProperty("correlationId").GetString(), code);
        }
    }

    [DockerFact]
    public async Task DetailOfEveryBusinessProblem_IsAFixedSentenceWithoutReceivedValues()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("100.00");
        const string canary = "CANARY-77c1";
        var key = $"{canary}-key";

        var responses = new[]
        {
            await client.PostEntryAsync(accountId, WriteClient.EntryBody("DEBIT", "55555.55", reference: canary), key),
            await client.PostEntryAsync(accountId, WriteClient.EntryBody("CREDIT", "1.00", "ZZZ", description: canary), key),
            await client.PostEntryAsync(accountId, $"{{\"type\":\"{canary}\",\"{canary}\":\"{canary}\"}}", key),
            await client.PostEntryAsync(accountId, WriteClient.EntryBody("CREDIT", "1.00", description: canary), key),
            await client.PostEntryAsync(accountId, WriteClient.EntryBody("CREDIT", "2.00", description: canary), key)
        };

        responses.Select(response => response.StatusCode).ShouldBe([422, 422, 400, 201, 422]);
        responses.Take(3).Append(responses[4]).ShouldAllBe(response => !response.Body.Contains(canary, StringComparison.Ordinal));
        responses.Take(3).Append(responses[4]).ShouldAllBe(response => !response.Body.Contains("55555.55", StringComparison.Ordinal));
    }
}
