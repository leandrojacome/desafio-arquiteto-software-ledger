using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Writes.Support;

namespace Ledger.Api.IntegrationTests.Writes.Entries;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Security")]
[Trait("Category", "Integration")]
public sealed class EntryAuthorizationTests(PostgresFixture postgres)
{
    private readonly WriteTestData _data = new(postgres);

    private static string Token(string scope, string? clientId = "integration-tests") =>
        TestTokenFactory.Create(scope: scope, clientId: clientId);

    [DockerFact]
    public async Task WithoutAToken_BothRoutesAnswerUnauthenticated()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("100.00");
        var original = await client.DebitAsync(accountId, "10.00");
        var anonymous = new WriteRequestOptions { Anonymous = true };

        var registration = await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("CREDIT", "1.00"),
            WriteClient.NewKey(),
            anonymous);
        var reversal = await client.PostReversalAsync(accountId, original.Text("entryId"), WriteClient.NewKey(), null, anonymous);

        registration.ShouldBeProblem(401, "UNAUTHENTICATED");
        reversal.ShouldBeProblem(401, "UNAUTHENTICATED");
        registration.Header("WWW-Authenticate").ShouldNotBeNull().ShouldStartWith("Bearer");
        registration.Header("X-Correlation-Id").ShouldNotBeNullOrWhiteSpace();
        (await _data.CountEntriesAsync(accountId)).ShouldBe(2);
    }

    [DockerFact]
    public async Task WithOnlyTheReadScope_BothRoutesAnswerForbiddenAndWriteNothing()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("100.00");
        var original = await client.DebitAsync(accountId, "10.00");
        var readOnly = new WriteRequestOptions { Token = Token("ledger.read") };

        var registration = await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("CREDIT", "1.00"),
            WriteClient.NewKey(),
            readOnly);
        var reversal = await client.PostReversalAsync(accountId, original.Text("entryId"), WriteClient.NewKey(), null, readOnly);

        var problem = registration.ShouldBeProblem(403, "FORBIDDEN");
        problem.GetProperty("title").GetString().ShouldBe("Acesso negado");
        reversal.ShouldBeProblem(403, "FORBIDDEN");
        (await _data.CountEntriesAsync(accountId)).ShouldBe(2);
        (await _data.BalanceAsync(accountId)).ShouldBe(90.00m);
    }

    [DockerTheory]
    [InlineData("ledger.writer")]
    [InlineData("LEDGER.WRITE")]
    [InlineData("ledger.write.extra")]
    [InlineData("")]
    public async Task WithAScopeThatOnlyLooksLikeTheWriteScope_TheRequestIsForbidden(string scope)
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();

        var response = await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("CREDIT", "1.00"),
            WriteClient.NewKey(),
            new WriteRequestOptions { Token = Token(scope) });

        response.ShouldBeProblem(403, "FORBIDDEN");
    }

    [DockerFact]
    public async Task WithTheWriteScopeAmongOthers_TheRequestIsAccepted()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();

        var onlyWrite = await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("CREDIT", "1.00"),
            WriteClient.NewKey(),
            new WriteRequestOptions { Token = Token("ledger.write") });
        var several = await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("CREDIT", "1.00"),
            WriteClient.NewKey(),
            new WriteRequestOptions { Token = Token("openid ledger.read ledger.write") });

        onlyWrite.StatusCode.ShouldBe(201, onlyWrite.Body);
        several.StatusCode.ShouldBe(201, several.Body);
    }

    [DockerFact]
    public async Task WithATokenThatHasNoClientId_TheRequestIsForbidden()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();

        var response = await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("CREDIT", "1.00"),
            WriteClient.NewKey(),
            new WriteRequestOptions { Token = Token("ledger.write", null) });

        response.ShouldBeProblem(403, "FORBIDDEN");
    }

    [DockerFact]
    public async Task WithAClientIdLongerThanTheColumn_BothRoutesAnswerForbiddenInsteadOfFailing()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("100.00");
        var original = await client.DebitAsync(accountId, "10.00");
        var oversized = new WriteRequestOptions { Token = Token("ledger.read ledger.write", new string('c', 5000)) };

        var registration = await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("CREDIT", "1.00"),
            WriteClient.NewKey(),
            oversized);
        var reversal = await client.PostReversalAsync(accountId, original.Text("entryId"), WriteClient.NewKey(), null, oversized);
        var creation = await client.PostAccountAsync(options: oversized);

        registration.ShouldBeProblem(403, "FORBIDDEN");
        reversal.ShouldBeProblem(403, "FORBIDDEN");
        creation.ShouldBeProblem(403, "FORBIDDEN");
        (await _data.CountEntriesAsync(accountId)).ShouldBe(2);
        (await _data.CountKeysAsync(accountId)).ShouldBe(2);
    }

    [DockerFact]
    public async Task WithAnExpiredOrForgedToken_TheRequestIsUnauthenticated()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();
        var expired = TestTokenFactory.Create(lifetime: TimeSpan.FromHours(-1));
        var forged = TestTokenFactory.Create(signingKey: "a-different-signing-key-with-more-than-32-chars");

        foreach (var token in new[] { expired, forged })
        {
            var response = await client.PostEntryAsync(
                accountId,
                WriteClient.EntryBody("CREDIT", "1.00"),
                WriteClient.NewKey(),
                new WriteRequestOptions { Token = token });

            response.ShouldBeProblem(401, "UNAUTHENTICATED");
        }

        (await _data.CountEntriesAsync(accountId)).ShouldBe(0);
    }

    [DockerFact]
    public async Task ForbiddenAndUnauthenticatedResponses_NeverRevealWhyOrWhatWasExpected()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();

        var forbidden = await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("CREDIT", "1.00"),
            WriteClient.NewKey(),
            new WriteRequestOptions { Token = Token("ledger.read") });
        var unauthenticated = await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("CREDIT", "1.00"),
            WriteClient.NewKey(),
            new WriteRequestOptions { Anonymous = true });

        forbidden.Body.ShouldNotContain("ledger.write");
        forbidden.Body.ShouldNotContain("ledger.read");
        unauthenticated.Body.ShouldNotContain("Bearer");
    }
}
