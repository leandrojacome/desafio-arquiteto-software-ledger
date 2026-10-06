using Ledger.Application;
using Ledger.Application.Abstractions;
using Ledger.Application.Accounts;
using Ledger.Application.Balances;
using Ledger.Application.Entries;
using Ledger.Application.Idempotency;
using Ledger.Application.Integrity;
using Ledger.Application.Outbox;
using Ledger.Application.Security;
using Ledger.Infrastructure.Messaging;
using Ledger.Infrastructure.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Ledger.Infrastructure.Tests.Observability;

[Trait("Category", "Unit")]
public sealed class ObservabilityRegistrationTests
{
    private static readonly Type[] TelemetryPorts =
    [
        typeof(IEntryTelemetry),
        typeof(IReadTelemetry),
        typeof(ISecurityTelemetry),
        typeof(IOutboxTelemetry),
        typeof(IIntegrityTelemetry),
        typeof(IWorkerLoopTelemetry)
    ];

    private static readonly Type[] HandlerTypes =
    [
        typeof(CreateAccountHandler),
        typeof(RegisterEntryHandler),
        typeof(ReverseEntryHandler),
        typeof(GetBalanceHandler),
        typeof(ListEntriesHandler),
        typeof(PublishOutboxBatchHandler),
        typeof(PruneOutboxHandler),
        typeof(MeasureOutboxHandler),
        typeof(RunIntegrityCheckHandler),
        typeof(RewrapAccountsHandler),
        typeof(RecordKeyActivationHandler)
    ];

    private static ServiceCollection WithTheOtherPorts()
    {
        var services = new ServiceCollection();

        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(Substitute.For<IUnitOfWork>());
        services.AddSingleton(Substitute.For<IIdGenerator>());
        services.AddSingleton(Substitute.For<IBalanceReader>());
        services.AddSingleton(Substitute.For<IStatementReader>());
        services.AddSingleton(Substitute.For<IStatementCursorProtector>());
        services.AddSingleton(Substitute.For<IHolderDocumentProtector>());
        services.AddSingleton(Substitute.For<IAccountKeyRewrapper>());
        services.AddSingleton(Substitute.For<IOutboxQueue>());
        services.AddSingleton(Substitute.For<IIdempotencyKeyPruner>());
        services.AddSingleton(new IdempotencyPruneSettings(TimeSpan.FromDays(35), 5000, TimeSpan.FromMinutes(10)));
        services.AddOptions<OutboxOptions>();
        services.AddSingleton(Substitute.For<IEventPublisher>());
        services.AddSingleton(Substitute.For<IWorkerHeartbeat>());
        services.AddSingleton(Substitute.For<IIntegritySessions>());
        services.AddSingleton(Substitute.For<IKeyProvider>());
        services.AddSingleton(Substitute.For<IAuditTrail>());
        services.AddSingleton(new BalanceReadSettings(TimeSpan.FromSeconds(5)));
        services.AddSingleton(
            new OutboxSettings(
                200,
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(5),
                5,
                1000,
                1_000_000,
                TimeSpan.FromDays(7),
                5000));

        return services;
    }

    [Fact]
    public void AddLedgerObservability_RegistersEveryTelemetryPortAsASingleton()
    {
        var services = WithTheOtherPorts().AddLedgerObservability();

        foreach (var port in TelemetryPorts)
        {
            var descriptor = services.Single(candidate => candidate.ServiceType == port);

            descriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);
        }
    }

    [Fact]
    public void AddLedgerObservability_ResolvesEveryPortToTheSameInstanceEveryTime()
    {
        using var provider = WithTheOtherPorts().AddLedgerObservability().BuildServiceProvider();

        foreach (var port in TelemetryPorts)
        {
            provider.GetRequiredService(port).ShouldBeSameAs(provider.GetRequiredService(port));
        }
    }

    [Fact]
    public void AddLedgerObservability_UsesTheProcessWideLedgerMeterAndSource()
    {
        using var provider = WithTheOtherPorts().AddLedgerObservability().BuildServiceProvider();

        var sources = provider.GetRequiredService<TelemetrySources>();

        sources.ShouldBeSameAs(TelemetrySources.Default);
        sources.Meter.ShouldBeSameAs(Telemetry.Meter);
        sources.Source.ShouldBeSameAs(Telemetry.Source);
    }

    [Fact]
    public void AddLedgerObservability_MakesEveryHandlerResolvableWithValidationOnBuild()
    {
        var services = WithTheOtherPorts().AddApplication().AddLedgerObservability();

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();

        foreach (var handler in HandlerTypes)
        {
            scope.ServiceProvider.GetRequiredService(handler).ShouldBeOfType(handler);
        }
    }

    [Fact]
    public void AddLedgerObservability_KeepsAPortThatTheHostAlreadyRegistered()
    {
        var custom = Substitute.For<IEntryTelemetry>();
        var services = WithTheOtherPorts();
        services.AddSingleton(custom);

        using var provider = services.AddLedgerObservability().BuildServiceProvider();

        provider.GetRequiredService<IEntryTelemetry>().ShouldBeSameAs(custom);
    }

    [Fact]
    public void AddLedgerObservability_KeepsAnExistingTimeProvider()
    {
        var fake = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var services = WithTheOtherPorts();
        services.AddSingleton<TimeProvider>(fake);

        using var provider = services.AddLedgerObservability().BuildServiceProvider();

        provider.GetRequiredService<TimeProvider>().ShouldBeSameAs(fake);
    }

    [Fact]
    public void AddLedgerObservability_CalledTwice_DoesNotDuplicateAnything()
    {
        var services = WithTheOtherPorts().AddLedgerObservability().AddLedgerObservability();

        foreach (var port in TelemetryPorts)
        {
            services.Count(candidate => candidate.ServiceType == port).ShouldBe(1);
        }
    }

    [Fact]
    public void AddLedgerObservability_ExposesTheDatabaseTelemetryForThePersistenceLayer()
    {
        using var provider = WithTheOtherPorts().AddLedgerObservability().BuildServiceProvider();

        provider.GetRequiredService<DbTelemetry>().ShouldBeSameAs(provider.GetRequiredService<DbTelemetry>());
    }
}
