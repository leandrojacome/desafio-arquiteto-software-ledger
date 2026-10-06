using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Writes.Support;

namespace Ledger.Api.IntegrationTests.Writes.Accounts;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class CreateAccountIdempotencyTests(PostgresFixture postgres)
{
    private const string Replayed = "Idempotent-Replayed";

    private readonly WriteTestData _data = new(postgres);

    private static WriteRequestOptions Keyed(string key) => new() { IdempotencyKey = key };

    [DockerFact]
    public async Task SameKeyAndSameBody_ReturnsTheOriginalAccountWithTheReplayHeader()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var key = WriteClient.NewKey();
        var body = WriteClient.AccountBody(overdraftLimit: "50.00");
        var before = await _data.CountAccountsAsync();

        var first = await client.PostAccountAsync(body, Keyed(key));
        var second = await client.PostAccountAsync(body, Keyed(key));

        first.StatusCode.ShouldBe(201, first.Body);
        first.HasHeader(Replayed).ShouldBeFalse();
        second.StatusCode.ShouldBe(201, second.Body);
        second.Header(Replayed).ShouldBe("true");
        second.Body.ShouldBe(first.Body);
        second.Header("Location").ShouldBe(first.Header("Location"));
        second.Header("Cache-Control").ShouldBe("no-store");
        (await _data.CountAccountsAsync()).ShouldBe(before + 1);
    }

    [DockerFact]
    public async Task TheReplay_AddsNoAuditEventAndNoSecondAccountRow()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var key = WriteClient.NewKey();
        var body = WriteClient.AccountBody();

        var first = await client.PostAccountAsync(body, Keyed(key));
        await client.PostAccountAsync(body, Keyed(key));
        await client.PostAccountAsync(body, Keyed(key));

        var audit = await _data.AuditRowsAsync(first.Text("accountId"), "account.created");

        audit.Count.ShouldBe(1);
    }

    [DockerFact]
    public async Task SameKeyWithAnotherOverdraftLimit_IsRefusedAsReusedAndCreatesNothing()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var key = WriteClient.NewKey();
        const string document = "123.456.789-09";

        await client.PostAccountAsync(WriteClient.AccountBody(document, overdraftLimit: "50.00"), Keyed(key));
        var before = await _data.CountAccountsAsync();

        var reused = await client.PostAccountAsync(WriteClient.AccountBody(document, overdraftLimit: "75.00"), Keyed(key));

        var problem = reused.ShouldBeProblem(422, "IDEMPOTENCY_KEY_REUSED");
        problem.GetProperty("detail").GetString().ShouldBe(
            "A chave de idempotência já foi usada com um corpo de requisição diferente por este chamador.");
        reused.HasHeader(Replayed).ShouldBeFalse();
        (await _data.CountAccountsAsync()).ShouldBe(before);
    }

    [DockerFact]
    public async Task SameKeyWithAnotherHolderDocument_IsRefusedAsReused()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var key = WriteClient.NewKey();

        await client.PostAccountAsync(WriteClient.AccountBody(), Keyed(key));
        var reused = await client.PostAccountAsync(WriteClient.AccountBody(), Keyed(key));

        reused.ShouldBeProblem(422, "IDEMPOTENCY_KEY_REUSED");
    }

    [DockerFact]
    public async Task TheSameDocumentWrittenAnotherWay_IsTheSameRequest()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var key = WriteClient.NewKey();

        var first = await client.PostAccountAsync(WriteClient.AccountBody("123.456.789-09"), Keyed(key));
        var second = await client.PostAccountAsync(WriteClient.AccountBody("12345678909"), Keyed(key));

        second.StatusCode.ShouldBe(201, second.Body);
        second.Header(Replayed).ShouldBe("true");
        second.Text("accountId").ShouldBe(first.Text("accountId"));
    }

    [DockerFact]
    public async Task WithoutTheKey_EveryRequestCreatesANewAccount()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var body = WriteClient.AccountBody();
        var before = await _data.CountAccountsAsync();

        var first = await client.PostAccountAsync(body);
        var second = await client.PostAccountAsync(body);

        first.StatusCode.ShouldBe(201, first.Body);
        second.StatusCode.ShouldBe(201, second.Body);
        second.Text("accountId").ShouldNotBe(first.Text("accountId"));
        first.HasHeader(Replayed).ShouldBeFalse();
        second.HasHeader(Replayed).ShouldBeFalse();
        (await _data.CountAccountsAsync()).ShouldBe(before + 2);
    }

    [DockerFact]
    public async Task DifferentKeysWithTheSameBody_CreateDifferentAccounts()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var body = WriteClient.AccountBody();

        var first = await client.PostAccountAsync(body, Keyed(WriteClient.NewKey()));
        var second = await client.PostAccountAsync(body, Keyed(WriteClient.NewKey()));

        first.StatusCode.ShouldBe(201, first.Body);
        second.StatusCode.ShouldBe(201, second.Body);
        second.Text("accountId").ShouldNotBe(first.Text("accountId"));
    }

    [DockerFact]
    public async Task TheKeyBelongsToTheClientThatSentIt()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var key = WriteClient.NewKey();
        var body = WriteClient.AccountBody();
        var other = new WriteRequestOptions
        {
            IdempotencyKey = key,
            Token = TestTokenFactory.Create(clientId: "billing-core")
        };

        var first = await client.PostAccountAsync(body, Keyed(key));
        var second = await client.PostAccountAsync(body, other);
        var again = await client.PostAccountAsync(body, other);

        second.StatusCode.ShouldBe(201, second.Body);
        second.HasHeader(Replayed).ShouldBeFalse();
        second.Text("accountId").ShouldNotBe(first.Text("accountId"));
        again.Header(Replayed).ShouldBe("true");
        again.Text("accountId").ShouldBe(second.Text("accountId"));
    }

    [DockerFact]
    public async Task ParallelRequestsWithTheSameKey_CreateOneAccountAndAnswerTheSameOne()
    {
        const int requests = 12;
        await using var factory = new WriteApiFactory(postgres, WriteApiFactory.WidePool);
        var client = new WriteClient(factory.CreateClient());
        var key = WriteClient.NewKey();
        var body = WriteClient.AccountBody();
        var before = await _data.CountAccountsAsync();

        var responses = await Task.WhenAll(
            Enumerable.Range(0, requests).Select(_ => Task.Run(() => client.PostAccountAsync(body, Keyed(key)))));

        responses.ShouldAllBe(response => response.StatusCode == 201);
        responses.Select(response => response.Text("accountId")).Distinct().Count().ShouldBe(1);
        responses.Count(response => !response.HasHeader(Replayed)).ShouldBe(1);
        responses.Count(response => response.Header(Replayed) == "true").ShouldBe(requests - 1);
        (await _data.CountAccountsAsync()).ShouldBe(before + 1);
    }

    [DockerFact]
    public async Task ARequestRefusedByValidation_DoesNotConsumeTheKey()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var key = WriteClient.NewKey();

        var refused = await client.PostAccountAsync(WriteClient.AccountBody(currency: "EUR"), Keyed(key));
        var accepted = await client.PostAccountAsync(WriteClient.AccountBody(), Keyed(key));

        refused.StatusCode.ShouldBe(400, refused.Body);
        accepted.StatusCode.ShouldBe(201, accepted.Body);
        accepted.HasHeader(Replayed).ShouldBeFalse();
    }

    [DockerTheory]
    [InlineData("has space")]
    [InlineData("tab\there")]
    public async Task AMalformedKey_IsABadRequestAndCreatesNothing(string key)
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var before = await _data.CountAccountsAsync();

        var response = await client.PostAccountAsync(options: new WriteRequestOptions { IdempotencyKey = key });

        response.ShouldBeValidationProblem().Select(item => item.Field).ShouldBe(["Idempotency-Key"]);
        (await _data.CountAccountsAsync()).ShouldBe(before);
    }

    [DockerFact]
    public async Task AKeyLongerThanTheLimit_IsABadRequest()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());

        var response = await client.PostAccountAsync(options: Keyed(new string('k', 129)));

        response.ShouldBeValidationProblem().ShouldBe([("Idempotency-Key", "TOO_LONG")]);
    }

    [DockerFact]
    public async Task AKeySentTwice_IsABadRequest()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());

        var response = await client.PostAccountAsync(options: new WriteRequestOptions
        {
            RepeatedIdempotencyKeys = [WriteClient.NewKey(), WriteClient.NewKey()]
        });

        response.ShouldBeValidationProblem().ShouldBe([("Idempotency-Key", "INVALID_FORMAT")]);
    }

    [DockerFact]
    public async Task AKeyIssueAndABodyIssue_ComeTogetherInOneBadRequest()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());

        var response = await client.PostAccountAsync("{}", Keyed("has space"));

        response.ShouldBeValidationProblem().ShouldBe(
        [
            ("holderDocument", "REQUIRED"),
            ("currency", "REQUIRED"),
            ("Idempotency-Key", "INVALID_FORMAT")
        ]);
    }

    [DockerFact]
    public async Task TheReplay_StillWorksAfterTheActiveKeyVersionMoves()
    {
        var key = WriteClient.NewKey();
        var body = WriteClient.AccountBody();
        string accountId;

        await using (var original = new WriteApiFactory(postgres))
        {
            var first = await new WriteClient(original.CreateClient()).PostAccountAsync(body, Keyed(key));
            first.StatusCode.ShouldBe(201, first.Body);
            accountId = first.Text("accountId");
        }

        var rotated = new Dictionary<string, string?> { ["Security:Pii:ActiveKeyVersion"] = "2" };

        await using var after = new WriteApiFactory(postgres, rotated);
        var replay = await new WriteClient(after.CreateClient()).PostAccountAsync(body, Keyed(key));

        replay.StatusCode.ShouldBe(201, replay.Body);
        replay.Header(Replayed).ShouldBe("true");
        replay.Text("accountId").ShouldBe(accountId);
    }

    [DockerFact]
    public async Task NothingStoredForTheKey_ContainsTheHolderDocument()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        const string document = "529.982.247-25";

        await client.PostAccountAsync(WriteClient.AccountBody(document), Keyed(WriteClient.NewKey()));

        var stored = await _data.EverythingStoredAsTextAsync();

        stored.ShouldNotContain("52998224725");
        stored.ShouldNotContain(document);
    }
}
