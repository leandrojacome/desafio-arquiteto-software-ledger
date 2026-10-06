using System.Net;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Domain.Accounts;

namespace Ledger.Api.IntegrationTests.Reads;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class StatementEmptyTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ReadWorld _world = null!;

    public Task InitializeAsync()
    {
        _world = ReadWorld.Create(postgres);

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _world.DisposeAsync();
    }

    [DockerFact]
    public async Task Statement_OfAnExistingAccountWithoutEntries_Returns200WithAnEmptyPage()
    {
        var accountId = await _world.Host.CreateAccountAsync();

        using var response = await _world.Client.StatementAsync(accountId);

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
        response.Json.GetProperty("items").GetArrayLength().ShouldBe(0);
        response.IsNull("nextCursor").ShouldBeTrue();
        response.Json.GetProperty("limit").GetInt32().ShouldBe(50);
    }

    [DockerFact]
    public async Task Statement_WithAWindowWithoutEntries_Returns200WithAnEmptyPage()
    {
        var accountId = await _world.Host.CreateFundedAccountAsync(10.00m);

        using var response = await _world.Client.StatementAsync(
            accountId,
            "from=2001-01-01T00:00:00Z&to=2001-01-02T00:00:00Z");

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
        response.Json.GetProperty("items").GetArrayLength().ShouldBe(0);
        response.IsNull("nextCursor").ShouldBeTrue();
    }

    [DockerFact]
    public async Task Statement_OfAnUnknownAccount_Returns404AccountNotFound()
    {
        var unknown = AccountId.From(Guid.CreateVersion7()).Value;

        using var response = await _world.Client.StatementAsync(unknown);

        response.ShouldBeProblem(
            HttpStatusCode.NotFound,
            "ACCOUNT_NOT_FOUND",
            "Conta não encontrada",
            ReadApiClient.StatementPath(unknown));
        response.Header("Cache-Control").ShouldBe("no-store");
    }

    [DockerFact]
    public async Task Statement_OfAnUnknownAccountWithAWindow_StillReturns404()
    {
        var unknown = AccountId.From(Guid.CreateVersion7()).Value;

        using var response = await _world.Client.StatementAsync(unknown, "from=2001-01-01T00:00:00Z&limit=5");

        response.ShouldBeProblem(HttpStatusCode.NotFound, "ACCOUNT_NOT_FOUND", "Conta não encontrada");
    }
}
