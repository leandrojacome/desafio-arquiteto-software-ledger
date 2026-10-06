using Ledger.Application.Abstractions;
using Ledger.Application.Accounts;
using Ledger.Application.Balances;
using Ledger.Application.Entries;
using Ledger.Application.Idempotency;
using Ledger.Application.Integrity;
using Ledger.Application.Outbox;
using Ledger.Application.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Ledger.Application.Tests;

[Trait("Category", "Unit")]
public sealed class DependencyInjectionTests
{
    private static readonly Type[] HandlerTypes =
    [
        typeof(CreateAccountHandler),
        typeof(RegisterEntryHandler),
        typeof(ReverseEntryHandler),
        typeof(GetBalanceHandler),
        typeof(ListEntriesHandler),
        typeof(PublishOutboxBatchHandler),
        typeof(PruneOutboxHandler),
        typeof(PruneIdempotencyKeysHandler),
        typeof(MeasureOutboxHandler),
        typeof(RunIntegrityCheckHandler),
        typeof(InspectAccountIntegrityHandler),
        typeof(RewrapAccountsHandler),
        typeof(RecordKeyActivationHandler)
    ];

    private static ServiceCollection WithEveryPort()
    {
        var services = new ServiceCollection();

        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(Substitute.For<IUnitOfWork>());
        services.AddSingleton(Substitute.For<IIdGenerator>());
        services.AddSingleton(Substitute.For<IEntryTelemetry>());
        services.AddSingleton(Substitute.For<IReadTelemetry>());
        services.AddSingleton(Substitute.For<IBalanceReader>());
        services.AddSingleton(Substitute.For<IStatementReader>());
        services.AddSingleton(Substitute.For<IStatementCursorProtector>());
        services.AddSingleton(Substitute.For<IHolderDocumentProtector>());
        services.AddSingleton(Substitute.For<ISecurityTelemetry>());
        services.AddSingleton(Substitute.For<IAccountKeyRewrapper>());
        services.AddSingleton(Substitute.For<IOutboxQueue>());
        services.AddSingleton(Substitute.For<IIdempotencyKeyPruner>());
        services.AddSingleton(new IdempotencyPruneSettings(TimeSpan.FromDays(35), 5000, TimeSpan.FromMinutes(10)));
        services.AddSingleton(Substitute.For<IEventPublisher>());
        services.AddSingleton(Substitute.For<IOutboxTelemetry>());
        services.AddSingleton(Substitute.For<IWorkerHeartbeat>());
        services.AddSingleton(Substitute.For<IIntegritySessions>());
        services.AddSingleton(Substitute.For<IIntegrityTelemetry>());
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
    public void AddApplication_RegistersTheSystemTimeProvider()
    {
        var services = new ServiceCollection().AddApplication();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<TimeProvider>().ShouldBeSameAs(TimeProvider.System);
    }

    [Fact]
    public void AddApplication_KeepsAnExistingTimeProvider()
    {
        var fake = new FakeTimeProvider();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(fake);

        using var provider = services.AddApplication().BuildServiceProvider();

        provider.GetRequiredService<TimeProvider>().ShouldBeSameAs(fake);
    }

    [Theory]
    [MemberData(nameof(Handlers))]
    public void AddApplication_RegistersEveryHandlerAsScoped(Type handlerType)
    {
        var services = new ServiceCollection().AddApplication();

        var descriptor = services.Single(candidate => candidate.ServiceType == handlerType);

        descriptor.Lifetime.ShouldBe(ServiceLifetime.Scoped);
    }

    [Theory]
    [MemberData(nameof(Handlers))]
    public void AddApplication_EveryHandlerIsResolvableOnceTheHostRegistersThePorts(Type handlerType)
    {
        var services = WithEveryPort().AddApplication();

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService(handlerType).ShouldBeOfType(handlerType);
    }

    [Fact]
    public void AddApplication_HandlerIsTheSameInsideAScopeAndDifferentAcrossScopes()
    {
        var services = WithEveryPort().AddApplication();
        using var provider = services.BuildServiceProvider();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var inFirst = first.ServiceProvider.GetRequiredService<RegisterEntryHandler>();

        first.ServiceProvider.GetRequiredService<RegisterEntryHandler>().ShouldBeSameAs(inFirst);
        second.ServiceProvider.GetRequiredService<RegisterEntryHandler>().ShouldNotBeSameAs(inFirst);
    }

    [Fact]
    public void AddApplication_RegistersTheReadAuditAsASingleton()
    {
        var services = WithEveryPort().AddApplication();
        using var provider = services.BuildServiceProvider();

        var descriptor = services.Single(candidate => candidate.ServiceType == typeof(ReadAudit));

        descriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);
        provider.GetRequiredService<ReadAudit>().ShouldBeSameAs(provider.GetRequiredService<ReadAudit>());
    }

    [Fact]
    public void AddApplication_DoesNotRegisterAnyPortOrSettings()
    {
        var services = new ServiceCollection().AddApplication();

        var registered = services.Select(descriptor => descriptor.ServiceType).ToList();

        registered.Where(type => type.IsInterface && type.Namespace == "Ledger.Application.Abstractions")
            .ShouldBeEmpty();
        registered.ShouldNotContain(typeof(BalanceReadSettings));
        registered.ShouldNotContain(typeof(OutboxSettings));
    }

    public static TheoryData<Type> Handlers()
    {
        var data = new TheoryData<Type>();

        foreach (var handlerType in HandlerTypes)
        {
            data.Add(handlerType);
        }

        return data;
    }
}
