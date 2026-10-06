using System.Diagnostics;
using System.Net;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application.Entries;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;
using Ledger.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ledger.Api.IntegrationTests.Reads;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
[Trait("Category", "Resilience")]
public sealed class ReadPoolIsolationTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly TimeSpan RefusalDeadline = TimeSpan.FromSeconds(3);

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
    public async Task Statement_WithItsPoolExhausted_Answers503WhileBalanceAndWritesKeepWorking()
    {
        var accountId = await _world.Host.CreateFundedAccountAsync(100.00m);
        var connections = _world.Factory.Services.GetRequiredService<IPostgresConnectionFactory>();

        await using (await ReadPoolHolder.HoldAsync(connections, PostgresSource.Statement, MaxPoolSize(PostgresSource.Statement)))
        {
            var stopwatch = Stopwatch.StartNew();
            using var statement = await _world.Client.StatementAsync(accountId);
            stopwatch.Stop();

            using var balance = await _world.Client.BalanceAsync(accountId);
            var write = await WriteThroughTheApiPoolAsync(accountId, "pool-statement");

            statement.ShouldBeProblem(HttpStatusCode.ServiceUnavailable, "SERVICE_UNAVAILABLE", "Serviço temporariamente indisponível");
            statement.Header("Retry-After").ShouldBe("1");
            stopwatch.Elapsed.ShouldBeLessThan(RefusalDeadline);
            balance.Status.ShouldBe(HttpStatusCode.OK, balance.Body);
            write.IsSuccess.ShouldBeTrue();
        }

        using var recovered = await _world.Client.StatementAsync(accountId);

        recovered.Status.ShouldBe(HttpStatusCode.OK, recovered.Body);
    }

    [DockerFact]
    public async Task Balance_WithItsPoolExhausted_Answers503WhileStatementKeepsWorking()
    {
        var accountId = await _world.Host.CreateFundedAccountAsync(100.00m);
        var connections = _world.Factory.Services.GetRequiredService<IPostgresConnectionFactory>();

        await using (await ReadPoolHolder.HoldAsync(connections, PostgresSource.Balance, MaxPoolSize(PostgresSource.Balance)))
        {
            var stopwatch = Stopwatch.StartNew();
            using var balance = await _world.Client.BalanceAsync(accountId);
            stopwatch.Stop();

            using var historical = await _world.Client.BalanceAtAsync(accountId, ReadClock.UtcNow);
            using var statement = await _world.Client.StatementAsync(accountId);

            balance.ShouldBeProblem(HttpStatusCode.ServiceUnavailable, "SERVICE_UNAVAILABLE", "Serviço temporariamente indisponível");
            balance.Header("Retry-After").ShouldBe("1");
            historical.Status.ShouldBe(HttpStatusCode.ServiceUnavailable);
            stopwatch.Elapsed.ShouldBeLessThan(RefusalDeadline);
            statement.Status.ShouldBe(HttpStatusCode.OK, statement.Body);
        }

        using var recovered = await _world.Client.BalanceAsync(accountId);

        recovered.Status.ShouldBe(HttpStatusCode.OK, recovered.Body);
    }

    private int MaxPoolSize(PostgresSource source) =>
        _world.Factory.Services.GetRequiredService<IOptions<PostgresOptions>>().Value.For(source).MaxPoolSize;

    private async Task<Result<EntryOutcome>> WriteThroughTheApiPoolAsync(Ledger.Domain.Accounts.AccountId accountId, string key)
    {
        await using var scope = _world.Factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<RegisterEntryHandler>();

        return await handler.HandleAsync(
            new RegisterEntryCommand(
                accountId,
                IdempotencyKey.From(key).Value,
                EntryType.Credit,
                Money.CreatePositive(5.00m, "BRL").Value,
                null,
                null,
                null,
                "pool-isolation",
                "pool-isolation-correlation",
                null),
            CancellationToken.None);
    }
}
