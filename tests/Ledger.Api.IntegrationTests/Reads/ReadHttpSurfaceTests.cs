using System.Net;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Domain.Accounts;

namespace Ledger.Api.IntegrationTests.Reads;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class ReadHttpSurfaceTests(PostgresFixture postgres) : IAsyncLifetime
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

    [DockerTheory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Balance_WithAMethodThatIsNotGet_Returns405WithAllowGet(string method)
    {
        var accountId = await _world.Host.CreateAccountAsync();

        using var response = await _world.Client.SendAsync(new HttpMethod(method), ReadApiClient.BalancePath(accountId));

        response.ShouldBeProblem(
            HttpStatusCode.MethodNotAllowed,
            "METHOD_NOT_ALLOWED",
            "Método não permitido",
            ReadApiClient.BalancePath(accountId));
        response.Header("Allow").ShouldBe("GET");
    }

    [DockerTheory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Statement_WithAMethodThatIsNeitherGetNorPost_Returns405AllowingGetAndPost(string method)
    {
        var accountId = await _world.Host.CreateAccountAsync();

        using var response = await _world.Client.SendAsync(new HttpMethod(method), ReadApiClient.StatementPath(accountId));

        response.ShouldBeProblem(HttpStatusCode.MethodNotAllowed, "METHOD_NOT_ALLOWED", "Método não permitido");

        var allowed = (response.Header("Allow") ?? string.Empty).Split(',', StringSplitOptions.TrimEntries);

        allowed.Order(StringComparer.Ordinal).ShouldBe(["GET", "POST"]);
    }

    [DockerFact]
    public async Task Reads_OnSuccessAndOnTheirOwnErrors_ForbidCachingAndCarryNoValidators()
    {
        var accountId = await _world.Host.CreateFundedAccountAsync(10.00m);
        var unknown = AccountId.From(Guid.CreateVersion7()).Value;

        using var balance = await _world.Client.BalanceAsync(accountId);
        using var historical = await _world.Client.BalanceAsOfAsync(accountId, "2026-01-15T10:00:00Z");
        using var statement = await _world.Client.StatementAsync(accountId);
        using var invalidAsOf = await _world.Client.BalanceAsOfAsync(accountId, "abc");
        using var invalidQuery = await _world.Client.StatementAsync(accountId, "limit=0");
        using var notFound = await _world.Client.BalanceAsync(unknown);
        using var statementNotFound = await _world.Client.StatementAsync(unknown);

        foreach (var response in new[] { balance, historical, statement, invalidAsOf, invalidQuery, notFound, statementNotFound })
        {
            response.Header("Cache-Control").ShouldBe("no-store");
            response.HasHeader("ETag").ShouldBeFalse();
            response.HasHeader("Last-Modified").ShouldBeFalse();
            response.Header("X-Correlation-Id").ShouldNotBeNullOrWhiteSpace();
        }
    }

    [DockerFact]
    public async Task Reads_AnswerHeadAndOptionsWith405()
    {
        var accountId = await _world.Host.CreateAccountAsync();

        using var head = await _world.Client.SendAsync(HttpMethod.Head, ReadApiClient.BalancePath(accountId));
        using var options = await _world.Client.SendAsync(HttpMethod.Options, ReadApiClient.StatementPath(accountId));

        head.Status.ShouldBe(HttpStatusCode.MethodNotAllowed);
        options.Status.ShouldBe(HttpStatusCode.MethodNotAllowed);
    }
}
