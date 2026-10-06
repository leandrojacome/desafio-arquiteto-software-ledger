using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Writes.Support;

namespace Ledger.Api.IntegrationTests.Writes.Accounts;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Security")]
[Trait("Category", "Integration")]
public sealed class AccountProvisioningTests(PostgresFixture postgres)
{
    private static readonly IReadOnlyDictionary<string, string?> OnlyProvisioner = new Dictionary<string, string?>
    {
        ["Authorization:AccountProvisioningClients:0"] = "account-provisioner"
    };

    private readonly WriteTestData _data = new(postgres);

    private static WriteRequestOptions As(string clientId, string scope = "ledger.read ledger.write") =>
        new() { Token = TestTokenFactory.Create(scope: scope, clientId: clientId) };

    [DockerFact]
    public async Task ClientOutsideTheList_IsForbiddenBeforeTheBodyIsReadAndNothingIsCreated()
    {
        await using var factory = new WriteApiFactory(postgres, OnlyProvisioner);
        var client = new WriteClient(factory.CreateClient());
        var before = await _data.CountAccountsAsync();

        var withValidBody = await client.PostAccountAsync(null, As("pix-gateway"));
        var withInvalidBody = await client.PostAccountAsync("{broken", As("pix-gateway"));
        var withHugeBody = await client.PostAccountAsync(new string('x', 20_000), As("pix-gateway"));

        withValidBody.ShouldBeProblem(403, "FORBIDDEN");
        withInvalidBody.ShouldBeProblem(403, "FORBIDDEN");
        withHugeBody.ShouldBeProblem(403, "FORBIDDEN");
        (await _data.CountAccountsAsync()).ShouldBe(before);
    }

    [DockerFact]
    public async Task ClientInTheList_CreatesTheAccount()
    {
        await using var factory = new WriteApiFactory(postgres, OnlyProvisioner);
        var client = new WriteClient(factory.CreateClient());

        var response = await client.PostAccountAsync(null, As("account-provisioner"));

        response.StatusCode.ShouldBe(201, response.Body);
    }

    [DockerFact]
    public async Task ClientInTheList_WithoutTheWriteScope_IsForbidden()
    {
        await using var factory = new WriteApiFactory(postgres, OnlyProvisioner);
        var client = new WriteClient(factory.CreateClient());

        var response = await client.PostAccountAsync(null, As("account-provisioner", "ledger.read"));

        response.ShouldBeProblem(403, "FORBIDDEN");
    }

    [DockerFact]
    public async Task WithoutAToken_TheAnswerIsUnauthenticated()
    {
        await using var factory = new WriteApiFactory(postgres, OnlyProvisioner);
        var client = new WriteClient(factory.CreateClient());

        var response = await client.PostAccountAsync(null, new WriteRequestOptions { Anonymous = true });

        response.ShouldBeProblem(401, "UNAUTHENTICATED");
    }

    [DockerFact]
    public async Task ListedClientIdIsComparedExactly()
    {
        await using var factory = new WriteApiFactory(postgres, OnlyProvisioner);
        var client = new WriteClient(factory.CreateClient());

        foreach (var lookalike in new[] { "Account-Provisioner", "account-provisioner ", "account-provisioner-2", "account" })
        {
            (await client.PostAccountAsync(null, As(lookalike))).ShouldBeProblem(403, "FORBIDDEN");
        }
    }

    [DockerFact]
    public async Task WildcardInTheList_AllowsAnyClientWithTheWriteScopeOutsideProduction()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());

        var anyClient = await client.PostAccountAsync(null, As("some-other-client"));
        var readOnly = await client.PostAccountAsync(null, As("some-other-client", "ledger.read"));

        anyClient.StatusCode.ShouldBe(201, anyClient.Body);
        readOnly.ShouldBeProblem(403, "FORBIDDEN");
    }

    [DockerFact]
    public async Task EntryRoutes_AreNotRestrictedToProvisioningClients()
    {
        await using var factory = new WriteApiFactory(postgres, OnlyProvisioner);
        var client = new WriteClient(factory.CreateClient());
        var created = await client.PostAccountAsync(null, As("account-provisioner"));
        var accountId = created.Text("accountId");

        var response = await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("CREDIT", "1.00"),
            WriteClient.NewKey(),
            As("pix-gateway"));

        response.StatusCode.ShouldBe(201, response.Body);
    }
}
