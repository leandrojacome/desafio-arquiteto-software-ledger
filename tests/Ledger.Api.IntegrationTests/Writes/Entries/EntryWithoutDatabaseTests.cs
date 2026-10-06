using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Writes.Support;

namespace Ledger.Api.IntegrationTests.Writes.Entries;

[Trait("Category", "Integration")]
public sealed class EntryWithoutDatabaseTests(LedgerApiFactory factory) : IClassFixture<LedgerApiFactory>
{
    private const string AnyAccount = "0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33";

    private WriteClient Client() => new(factory.CreateClient());

    [Fact]
    public async Task EveryAnswerThatDoesNotNeedTheDatabase_ComesWithoutAskingForIt()
    {
        var client = Client();
        var readOnly = TestTokenFactory.Create(scope: "ledger.read");

        var cases = new (int Status, string Code, ApiResponse Response)[]
        {
            (401, "UNAUTHENTICATED", await client.PostEntryAsync(AnyAccount, "{}", null, new WriteRequestOptions { Anonymous = true })),
            (403, "FORBIDDEN", await client.PostEntryAsync(AnyAccount, "{}", null, new WriteRequestOptions { Token = readOnly })),
            (404, "ACCOUNT_NOT_FOUND", await client.PostEntryAsync("not-a-guid", WriteClient.EntryBody("CREDIT", "1.00"), WriteClient.NewKey())),
            (404, "ENTRY_NOT_FOUND", await client.PostReversalAsync(AnyAccount, "not-a-guid", WriteClient.NewKey())),
            (400, "VALIDATION_FAILED", await client.PostEntryAsync(AnyAccount, "{}", WriteClient.NewKey())),
            (400, "IDEMPOTENCY_KEY_REQUIRED", await client.PostEntryAsync(AnyAccount, WriteClient.EntryBody("CREDIT", "1.00"), null)),
            (413, "PAYLOAD_TOO_LARGE", await client.PostEntryAsync(AnyAccount, new string('x', 17_000), WriteClient.NewKey())),
            (415, "UNSUPPORTED_MEDIA_TYPE", await client.PostEntryAsync(AnyAccount, "{}", WriteClient.NewKey(), new WriteRequestOptions { ContentType = "text/plain" })),
            (405, "METHOD_NOT_ALLOWED", await client.SendAsync(HttpMethod.Delete, $"/v1/accounts/{AnyAccount}/entries"))
        };

        foreach (var (status, code, response) in cases)
        {
            response.ShouldBeProblem(status, code);
        }
    }

    [Fact]
    public async Task AccountCreation_RejectsBadInputWithoutTheDatabase()
    {
        var client = Client();

        (await client.PostAccountAsync("{}")).ShouldBeValidationProblem();
        (await client.PostAccountAsync(null, new WriteRequestOptions { Anonymous = true })).ShouldBeProblem(401, "UNAUTHENTICATED");
        (await client.PostAccountAsync(null, new WriteRequestOptions { ContentType = "text/plain" })).ShouldBeProblem(415, "UNSUPPORTED_MEDIA_TYPE");
    }

    [Fact]
    public async Task ValidRequestWithTheDatabaseUnreachable_Is503WithRetryAfterAndNoInternalDetail()
    {
        var client = Client();

        var registration = await client.CreditAsync(AnyAccount, "1.00");
        var reversal = await client.PostReversalAsync(AnyAccount, AnyAccount, WriteClient.NewKey());
        var account = await client.PostAccountAsync();

        foreach (var response in new[] { registration, reversal, account })
        {
            response.ShouldBeProblem(503, "SERVICE_UNAVAILABLE");
            response.Header("Retry-After").ShouldBe("1");
            response.Body.ShouldNotContain("127.0.0.1");
            response.Body.ShouldNotContain("password", Case.Insensitive);
            response.DescriptiveTexts().ShouldAllBe(text => !text.Contains("5432", StringComparison.Ordinal));
        }
    }
}
