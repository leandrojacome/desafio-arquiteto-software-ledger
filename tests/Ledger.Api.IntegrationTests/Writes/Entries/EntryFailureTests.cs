using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Observability;
using Ledger.Api.IntegrationTests.Writes.Support;
using Ledger.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ledger.Api.IntegrationTests.Writes.Entries;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class EntryFailureTests(PostgresFixture postgres)
{
    private static Action<IServiceCollection> FailingWith(Exception failure) => services =>
    {
        services.RemoveAll<IUnitOfWork>();
        services.AddScoped<IUnitOfWork>(_ => new ThrowingUnitOfWork(failure));
    };

    [DockerFact]
    public async Task TransientFailure_OnRegistration_Is503WithRetryAfterAndNoDetail()
    {
        await using var factory = new WriteApiFactory(
            postgres,
            configureServices: FailingWith(new TimeoutException("connection reset by 10.1.2.3 password=hunter2")));
        var client = new WriteClient(factory.CreateClient());

        var response = await client.CreditAsync(Guid.NewGuid().ToString("D"), "10.00");

        var problem = response.ShouldBeProblem(503, "SERVICE_UNAVAILABLE");
        problem.GetProperty("title").GetString().ShouldBe("Serviço temporariamente indisponível");
        response.Header("Retry-After").ShouldBe("1");
        response.Body.ShouldNotContain("10.1.2.3");
        response.Body.ShouldNotContain("hunter2");
    }

    [DockerFact]
    public async Task TransientFailure_OnReversal_Is503WithRetryAfter()
    {
        await using var factory = new WriteApiFactory(postgres, configureServices: FailingWith(new TimeoutException()));
        var client = new WriteClient(factory.CreateClient());

        var response = await client.PostReversalAsync(
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"),
            WriteClient.NewKey());

        response.ShouldBeProblem(503, "SERVICE_UNAVAILABLE");
        response.Header("Retry-After").ShouldBe("1");
    }

    [DockerFact]
    public async Task UnexpectedFailure_Is500WithoutAnyInternalDetail()
    {
        await using var factory = new WriteApiFactory(
            postgres,
            configureServices: FailingWith(new InvalidOperationException("secret internal state: table ledger_entries")));
        var client = new WriteClient(factory.CreateClient());

        var response = await client.CreditAsync(Guid.NewGuid().ToString("D"), "10.00");

        var problem = response.ShouldBeProblem(500, "INTERNAL_ERROR");
        problem.GetProperty("title").GetString().ShouldBe("Erro interno");
        response.HasHeader("Retry-After").ShouldBeFalse();
        response.Body.ShouldNotContain("secret");
        response.Body.ShouldNotContain("ledger_entries");
    }

    [DockerFact]
    public async Task KeyProviderOutage_OnAccountCreation_Is503WithRetryAfterAndTheRestKeepsWorking()
    {
        var outage = new OutageSwitch();
        var sink = new CapturingLogSink();
        await using var factory = new WriteApiFactory(
            postgres,
            configureServices: services => services.Decorate<IHolderDocumentProtector>(
                (inner, _) => new SwitchedProtector(inner, outage)),
            logSink: sink);
        var client = new WriteClient(factory.CreateClient());
        var other = await client.CreateFundedAccountAsync("100.00");
        var data = new WriteTestData(postgres);
        var accountsBefore = await data.CountAccountsAsync();

        outage.Available = false;

        var refused = await client.PostAccountAsync();
        var entry = await client.DebitAsync(other, "10.00");

        refused.ShouldBeProblem(503, "SERVICE_UNAVAILABLE");
        refused.Header("Retry-After").ShouldBe("1");
        entry.StatusCode.ShouldBe(201, entry.Body);
        (await data.CountAccountsAsync()).ShouldBe(accountsBefore);

        sink.Events.ShouldContain(logEvent => CapturingLogSink.Property(logEvent, "EventId").Contains("7002", StringComparison.Ordinal));

        outage.Available = true;

        (await client.PostAccountAsync()).StatusCode.ShouldBe(201);
    }
}
