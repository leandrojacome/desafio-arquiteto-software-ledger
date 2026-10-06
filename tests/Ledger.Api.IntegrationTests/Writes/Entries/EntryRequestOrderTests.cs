using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Writes.Support;

namespace Ledger.Api.IntegrationTests.Writes.Entries;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class EntryRequestOrderTests(PostgresFixture postgres)
{
    private const string InvalidBody = "{\"type\":\"nope\"}";

    [DockerFact]
    public async Task AccountIdThatIsNotAGuid_IsNotFoundEvenWithAnInvalidBodyAndNoKey()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());

        var response = await client.PostEntryAsync("not-a-guid", InvalidBody, null);

        response.ShouldBeProblem(404, "ACCOUNT_NOT_FOUND");
    }

    [DockerFact]
    public async Task InvalidBodyWithoutKey_IsAValidationFailureAndNotAMissingKey()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();

        var response = await client.PostEntryAsync(accountId, InvalidBody, null);

        response.ShouldBeValidationProblem().ShouldContain(("type", "NOT_ALLOWED"));
    }

    [DockerFact]
    public async Task ValidBodyWithoutKey_IsAMissingKey()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();

        var response = await client.PostEntryAsync(accountId, WriteClient.EntryBody("CREDIT", "1.00"), null);

        var problem = response.ShouldBeProblem(400, "IDEMPOTENCY_KEY_REQUIRED");
        problem.GetProperty("title").GetString().ShouldBe("Cabeçalho 'Idempotency-Key' obrigatório");
    }

    [DockerFact]
    public async Task ValidRequestForAnAccountThatDoesNotExist_IsNotFoundOnlyAfterTheValidation()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var missing = Guid.NewGuid().ToString("D");

        (await client.PostEntryAsync(missing, InvalidBody, WriteClient.NewKey()))
            .ShouldBeValidationProblem().ShouldContain(("type", "NOT_ALLOWED"));
        (await client.PostEntryAsync(missing, WriteClient.EntryBody("CREDIT", "1.00"), null))
            .ShouldBeProblem(400, "IDEMPOTENCY_KEY_REQUIRED");
        (await client.PostEntryAsync(missing, WriteClient.EntryBody("CREDIT", "1.00"), WriteClient.NewKey()))
            .ShouldBeProblem(404, "ACCOUNT_NOT_FOUND");
    }

    [DockerFact]
    public async Task WrongContentType_ComesBeforeTheAccountAndTheBody()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());

        var response = await client.PostEntryAsync(
            "not-a-guid",
            InvalidBody,
            null,
            new WriteRequestOptions { ContentType = "text/plain" });

        response.ShouldBeProblem(415, "UNSUPPORTED_MEDIA_TYPE");
    }

    [DockerFact]
    public async Task OversizedBody_ComesBeforeTheAccountAndTheContentType()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var body = new string('x', 16 * 1024 + 1);

        var response = await client.PostEntryAsync(
            "not-a-guid",
            body,
            null,
            new WriteRequestOptions { ContentType = "text/plain" });

        response.ShouldBeProblem(413, "PAYLOAD_TOO_LARGE");
    }

    [DockerFact]
    public async Task Authorization_ComesBeforeEverythingElse()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var readOnly = TestTokenFactory.Create(scope: "ledger.read");

        (await client.PostEntryAsync("not-a-guid", InvalidBody, null, new WriteRequestOptions { Anonymous = true }))
            .ShouldBeProblem(401, "UNAUTHENTICATED");
        (await client.PostEntryAsync("not-a-guid", InvalidBody, null, new WriteRequestOptions { Token = readOnly }))
            .ShouldBeProblem(403, "FORBIDDEN");
    }

    [DockerFact]
    public async Task Reversal_ChecksTheAccountBeforeTheEntryAndBothBeforeTheBodyAndTheKey()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();
        var anyGuid = Guid.NewGuid().ToString("D");

        (await client.PostReversalAsync("nope", "nope", null, "{broken"))
            .ShouldBeProblem(404, "ACCOUNT_NOT_FOUND");
        (await client.PostReversalAsync(accountId, "nope", null, "{broken"))
            .ShouldBeProblem(404, "ENTRY_NOT_FOUND");
        (await client.PostReversalAsync(accountId, anyGuid, null, "{broken"))
            .ShouldBeValidationProblem().ShouldBe([("$", "INVALID_JSON")]);
        (await client.PostReversalAsync(accountId, anyGuid, null))
            .ShouldBeProblem(400, "IDEMPOTENCY_KEY_REQUIRED");
        (await client.PostReversalAsync(accountId, anyGuid, WriteClient.NewKey()))
            .ShouldBeProblem(404, "ENTRY_NOT_FOUND");
    }

    [DockerFact]
    public async Task Reversal_WithAnEmptyBodyAndNoContentType_IsNotAnUnsupportedMediaType()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();

        var response = await client.PostReversalAsync(
            accountId,
            Guid.NewGuid().ToString("D"),
            WriteClient.NewKey(),
            null,
            new WriteRequestOptions { ContentType = null });

        response.ShouldBeProblem(404, "ENTRY_NOT_FOUND");
    }

    [DockerFact]
    public async Task Reversal_WithABodyButAnotherContentType_IsAnUnsupportedMediaType()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();

        var response = await client.PostReversalAsync(
            accountId,
            Guid.NewGuid().ToString("D"),
            WriteClient.NewKey(),
            "{\"description\":\"x\"}",
            new WriteRequestOptions { ContentType = "text/plain" });

        response.ShouldBeProblem(415, "UNSUPPORTED_MEDIA_TYPE");
    }
}
