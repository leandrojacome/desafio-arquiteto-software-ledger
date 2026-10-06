using System.Text;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Writes.Support;

namespace Ledger.Api.IntegrationTests.Writes.Entries;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class EntryHttpSurfaceTests(PostgresFixture postgres)
{
    private const int BodyLimit = 16 * 1024;

    private readonly WriteTestData _data = new(postgres);

    [DockerTheory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task OtherMethods_OnTheRegistrationRoute_AreNotAllowedAndChangeNothing(string method)
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("100.00");

        var response = await client.SendAsync(
            new HttpMethod(method),
            $"/v1/accounts/{accountId}/entries",
            WriteClient.EntryBody("DEBIT", "10.00"),
            new WriteRequestOptions { IdempotencyKey = WriteClient.NewKey() });

        response.ShouldBeProblem(405, "METHOD_NOT_ALLOWED");
        response.Header("Allow")
            .ShouldNotBeNull()
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ShouldBe(["GET", "POST"], ignoreOrder: true);
        (await _data.CountEntriesAsync(accountId)).ShouldBe(1);
        (await _data.BalanceAsync(accountId)).ShouldBe(100.00m);
    }

    [DockerTheory]
    [InlineData("GET")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task OtherMethods_OnTheReversalRoute_AreNotAllowedWithAllowPost(string method)
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("100.00");
        var original = await client.DebitAsync(accountId, "10.00");

        var response = await client.SendAsync(
            new HttpMethod(method),
            $"/v1/accounts/{accountId}/entries/{original.Text("entryId")}/reversals",
            null,
            new WriteRequestOptions { IdempotencyKey = WriteClient.NewKey() });

        response.ShouldBeProblem(405, "METHOD_NOT_ALLOWED");
        response.Header("Allow").ShouldBe("POST");
        (await _data.CountEntriesAsync(accountId)).ShouldBe(2);
    }

    [DockerTheory]
    [InlineData("DELETE")]
    [InlineData("PUT")]
    public async Task OtherMethods_OnAnEntry_AreNotMappedAtAll(string method)
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("100.00");
        var original = await client.DebitAsync(accountId, "10.00");

        var response = await client.SendAsync(
            new HttpMethod(method),
            $"/v1/accounts/{accountId}/entries/{original.Text("entryId")}",
            null);

        response.StatusCode.ShouldBeOneOf(404, 405);
        (await _data.CountEntriesAsync(accountId)).ShouldBe(2);
    }

    [DockerTheory]
    [InlineData("text/plain")]
    [InlineData("application/xml")]
    [InlineData("application/x-www-form-urlencoded")]
    [InlineData("application/json; charset=iso-8859-1")]
    [InlineData("application/jsonx")]
    [InlineData("json")]
    [InlineData(null)]
    public async Task ContentType_OtherThanJson_IsUnsupportedOnTheThreeRoutes(string? contentType)
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();
        var options = new WriteRequestOptions { ContentType = contentType };

        var registration = await client.PostEntryAsync(accountId, WriteClient.EntryBody("CREDIT", "1.00"), WriteClient.NewKey(), options);
        var account = await client.PostAccountAsync(null, options);

        var problem = registration.ShouldBeProblem(415, "UNSUPPORTED_MEDIA_TYPE");
        problem.GetProperty("title").GetString().ShouldBe("Tipo de mídia não suportado");
        problem.GetProperty("detail").GetString().ShouldBe("O 'Content-Type' deve ser 'application/json'.");
        account.ShouldBeProblem(415, "UNSUPPORTED_MEDIA_TYPE");
        (await _data.CountEntriesAsync(accountId)).ShouldBe(0);
    }

    [DockerTheory]
    [InlineData("application/json")]
    [InlineData("application/json; charset=utf-8")]
    [InlineData("application/json;charset=UTF-8")]
    [InlineData("APPLICATION/JSON")]
    public async Task ContentType_Json_WithOrWithoutUtf8_IsAccepted(string contentType)
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();

        var response = await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("CREDIT", "1.00"),
            WriteClient.NewKey(),
            new WriteRequestOptions { ContentType = contentType });

        response.StatusCode.ShouldBe(201, response.Body);
    }

    [DockerFact]
    public async Task Body_AboveSixteenKilobytes_IsTooLargeOnTheThreeRoutes()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("100.00");
        var original = await client.DebitAsync(accountId, "10.00");
        var huge = "{\"description\":\"" + new string('d', BodyLimit) + "\"}";

        var registration = await client.PostEntryAsync(accountId, huge, WriteClient.NewKey());
        var reversal = await client.PostReversalAsync(accountId, original.Text("entryId"), WriteClient.NewKey(), huge);
        var account = await client.PostAccountAsync(huge);

        var problem = registration.ShouldBeProblem(413, "PAYLOAD_TOO_LARGE");
        problem.GetProperty("title").GetString().ShouldBe("Corpo da requisição grande demais");
        problem.GetProperty("detail").GetString().ShouldBe("O corpo da requisição não pode exceder 16384 bytes.");
        reversal.ShouldBeProblem(413, "PAYLOAD_TOO_LARGE");
        account.ShouldBeProblem(413, "PAYLOAD_TOO_LARGE");
        (await _data.CountEntriesAsync(accountId)).ShouldBe(2);
    }

    [DockerFact]
    public async Task Body_ExactlyAtTheLimitInBytes_PassesTheSizeCheckAndOneByteMoreDoesNot()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();
        var padding = new string('ç', (BodyLimit - 2) / 2);
        var atLimit = "\"" + padding + "\"";

        Encoding.UTF8.GetByteCount(atLimit).ShouldBe(BodyLimit);

        var passes = await client.PostEntryAsync(accountId, atLimit, WriteClient.NewKey());
        var exceeds = await client.PostEntryAsync(accountId, atLimit + " ", WriteClient.NewKey());

        passes.ShouldBeValidationProblem().ShouldBe([("$", "INVALID_JSON")]);
        exceeds.ShouldBeProblem(413, "PAYLOAD_TOO_LARGE");
    }

    [DockerFact]
    public async Task EveryResponse_CarriesNoStoreAndTheCorrelationId()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("100.00");
        const string correlation = "c0ffee00c0ffee00c0ffee00c0ffee00";
        var options = new WriteRequestOptions { CorrelationId = correlation };

        var responses = new List<ApiResponse>
        {
            await client.PostEntryAsync(accountId, WriteClient.EntryBody("CREDIT", "1.00"), WriteClient.NewKey(), options),
            await client.PostEntryAsync(accountId, WriteClient.EntryBody("DEBIT", "9999.00"), WriteClient.NewKey(), options),
            await client.PostEntryAsync(accountId, "{broken", WriteClient.NewKey(), options),
            await client.PostEntryAsync(Guid.NewGuid().ToString("D"), WriteClient.EntryBody("CREDIT", "1.00"), WriteClient.NewKey(), options),
            await client.PostEntryAsync(accountId, WriteClient.EntryBody("CREDIT", "1.00"), null, options),
            await client.PostAccountAsync(WriteClient.AccountBody(), options)
        };

        responses.Select(response => response.StatusCode).ShouldBe([201, 422, 400, 404, 400, 201]);
        responses.ShouldAllBe(response => response.Header("Cache-Control") == "no-store");
        responses.ShouldAllBe(response => response.Header("X-Correlation-Id") == correlation);
        responses.Skip(1).Take(4).ShouldAllBe(response => response.Json().GetProperty("correlationId").GetString() == correlation);
    }
}
