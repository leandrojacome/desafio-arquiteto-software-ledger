using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application;
using Ledger.Application.Abstractions;
using Ledger.Application.Accounts;
using Ledger.Application.Entries;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;
using Ledger.Infrastructure.Health;
using Ledger.Infrastructure.Observability;
using Ledger.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ledger.Api.IntegrationTests.Persistence.Support;

internal sealed class LedgerHost : IAsyncDisposable
{
    public const string ClientId = "integration-tests";
    public const string Currency = "BRL";
    public const string CorrelationId = "integration-tests-correlation";

    private readonly ServiceProvider _provider;

    private LedgerHost(ServiceProvider provider, PostgresFixture postgres, CapturingLoggerProvider logs, TimeProvider time)
    {
        _provider = provider;
        Postgres = postgres;
        Logs = logs;
        Time = time;
    }

    public static IReadOnlyDictionary<string, string?> WideWritePool { get; } = new Dictionary<string, string?>
    {
        ["Postgres:Sources:Write:MaxPoolSize"] = "64",
        ["Postgres:Sources:Write:ConnectionTimeoutSeconds"] = "10",
        ["Postgres:Sources:Write:CommandTimeoutSeconds"] = "15",
        ["Postgres:Sources:Write:LockTimeoutMs"] = "10000",
        ["Postgres:Sources:Write:StatementTimeoutMs"] = "15000"
    };

    public PostgresFixture Postgres { get; }

    public CapturingLoggerProvider Logs { get; }

    public TimeProvider Time { get; }

    public IPostgresConnectionFactory Connections => _provider.GetRequiredService<IPostgresConnectionFactory>();

    public static LedgerHost Create(
        PostgresFixture postgres,
        IReadOnlyDictionary<string, string?>? overrides = null,
        TimeProvider? time = null)
    {
        var timeProvider = time ?? TimeProvider.System;
        var logs = new CapturingLoggerProvider();
        var settings = new Dictionary<string, string?>(PostgresFixture.ProductionSources);

        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(postgres.ConfigurationWith(settings))
            .Build();

        var services = new ServiceCollection();

        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug).AddProvider(logs));
        services.AddSingleton(timeProvider);
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(Environments.Development));
        services.AddSingleton<IWorkerHeartbeat>(provider => new WorkerHeartbeat(provider.GetRequiredService<TimeProvider>()));
        services.AddApplication();
        services.AddLedgerPersistence(
            configuration,
            [PostgresSource.Write, PostgresSource.Balance, PostgresSource.Statement]);
        services.AddLedgerObservability();

        return new LedgerHost(services.BuildServiceProvider(), postgres, logs, timeProvider);
    }

    public T Service<T>()
        where T : notnull => _provider.GetRequiredService<T>();

    public AsyncServiceScope CreateScope() => _provider.CreateAsyncScope();

    public async Task<AccountId> CreateAccountAsync(
        decimal overdraftLimit = 0m,
        string currency = Currency,
        CancellationToken cancellationToken = default)
    {
        await using var scope = CreateScope();
        var idGenerator = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var accountId = AccountId.From(idGenerator.NewId()).Value;
        var limit = Money.Create(overdraftLimit, currency).Value;
        var document = FakeProtectedDocument.Create(1);
        var account = new NewAccount(accountId, currency, limit, document);

        var created = await unitOfWork.ExecuteAsync(
            async (unit, token) => Result.Success(await unit.Accounts.CreateAsync(account, token) is not null),
            cancellationToken);

        created.Value.ShouldBeTrue();

        return accountId;
    }

    public async Task<AccountId> CreateFundedAccountAsync(
        decimal balance,
        decimal overdraftLimit = 0m,
        CancellationToken cancellationToken = default)
    {
        var accountId = await CreateAccountAsync(overdraftLimit, Currency, cancellationToken);

        var seed = await RegisterAsync(
            accountId,
            $"seed-{Guid.NewGuid():N}",
            EntryType.Credit,
            balance,
            cancellationToken: cancellationToken);

        seed.IsSuccess.ShouldBeTrue();

        return accountId;
    }

    public async Task<Result<EntryOutcome>> RegisterAsync(
        AccountId accountId,
        string key,
        EntryType type,
        decimal amount,
        string currency = Currency,
        DateTimeOffset? occurredAt = null,
        string? description = null,
        string? reference = null,
        string clientId = ClientId,
        string? traceParent = null,
        CancellationToken cancellationToken = default)
    {
        await using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<RegisterEntryHandler>();

        var command = new RegisterEntryCommand(
            accountId,
            IdempotencyKey.From(key).Value,
            type,
            Money.CreatePositive(amount, currency).Value,
            occurredAt,
            description,
            reference,
            clientId,
            CorrelationId,
            traceParent);

        return await handler.HandleAsync(command, cancellationToken);
    }

    public async Task<Result<EntryOutcome>> ReverseAsync(
        AccountId accountId,
        EntryId originalEntryId,
        string key,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        await using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ReverseEntryHandler>();

        var command = new ReverseEntryCommand(
            accountId,
            originalEntryId,
            IdempotencyKey.From(key).Value,
            description,
            ClientId,
            CorrelationId,
            null);

        return await handler.HandleAsync(command, cancellationToken);
    }

    public async Task<Result<TValue>> ExecuteAsync<TValue>(
        Func<IUnitOfWorkScope, CancellationToken, Task<Result<TValue>>> work,
        CancellationToken cancellationToken = default)
        where TValue : notnull
    {
        await using var scope = CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        return await unitOfWork.ExecuteAsync(work, cancellationToken);
    }

    public async Task<TResult> InUnitOfWorkAsync<TResult>(
        Func<IUnitOfWorkScope, CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken = default)
        where TResult : notnull
    {
        await using var scope = CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var result = await unitOfWork.ExecuteAsync(
            async (unit, token) => Result.Success(await work(unit, token)),
            cancellationToken);

        return result.Value;
    }

    public ValueTask DisposeAsync() => _provider.DisposeAsync();
}
