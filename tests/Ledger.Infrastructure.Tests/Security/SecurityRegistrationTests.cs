using Ledger.Application.Abstractions;
using Ledger.Application.Audit;
using Ledger.Application.Security;
using Ledger.Application.Tests.Security;
using Ledger.Domain.Accounts;
using Ledger.Infrastructure.Audit;
using Ledger.Infrastructure.Persistence;
using Ledger.Infrastructure.Resilience;
using Ledger.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using NSubstitute;

namespace Ledger.Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public sealed class SecurityRegistrationTests
{
    private static readonly PostgresSource[] ApiSources =
        [PostgresSource.Write, PostgresSource.Balance, PostgresSource.Statement];

    private static readonly PostgresSource[] WorkerSources = [PostgresSource.Worker];

    private static Dictionary<string, string?> ConfigurationProviderValues() => new()
    {
        ["Security:Pii:Provider"] = "Configuration",
        ["Security:Pii:ActiveKeyVersion"] = "1",
        ["Security:Pii:KeySets:1:EncryptionKey"] = SecurityVectors.EncryptionKeyBase64,
        ["Security:Pii:KeySets:1:BlindIndexKey"] = SecurityVectors.BlindIndexKeyBase64,
        ["Security:Cursor:SigningKey"] = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA="
    };

    private static ServiceCollection Services(
        IReadOnlyDictionary<string, string?> values,
        PostgresSource[] sources,
        IPostgresConnectionFactory? connections = null,
        bool classifierRegisteredBefore = false)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Testing");

        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddOptions();
        services.AddSingleton(environment);
        services.AddSingleton(Substitute.For<ISecurityTelemetry>());
        services.AddSingleton(connections ?? new RefusingConnectionFactory());

        if (classifierRegisteredBefore)
        {
            services.AddLedgerFailureClassification();
        }

        services.AddLedgerSecurity(configuration, sources);

        if (!classifierRegisteredBefore)
        {
            services.AddLedgerFailureClassification();
        }

        return services;
    }

    private static ServiceProvider Build(ServiceCollection services) =>
        services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

    [Fact]
    public async Task AddLedgerSecurity_ApiHost_ResolvesEveryPortOfTheFront()
    {
        await using var provider = Build(Services(ConfigurationProviderValues(), ApiSources));

        provider.GetRequiredService<IKeyProvider>().ShouldBeOfType<ConfigurationKeyProvider>();
        provider.GetRequiredService<IHolderDocumentProtector>().ShouldBeOfType<HolderDocumentProtector>();
        provider.GetRequiredService<IStatementCursorProtector>().ShouldBeOfType<HmacStatementCursorProtector>();
        provider.GetRequiredService<IAuditTrail>().ShouldBeOfType<PostgresAuditTrail>();
        provider.GetRequiredService<IDeniedWriteAuditor>().ShouldBeOfType<DeniedWriteAuditor>();
        provider.GetRequiredService<IAccountKeyRewrapper>().ShouldBeOfType<PostgresAccountKeyRewrapper>();
    }

    [Fact]
    public void AddLedgerSecurity_EverythingIsSingleton()
    {
        var services = Services(ConfigurationProviderValues(), ApiSources);

        foreach (var serviceType in new[]
                 {
                     typeof(IKeyProvider), typeof(IHolderDocumentProtector), typeof(IStatementCursorProtector),
                     typeof(IAuditTrail), typeof(IDeniedWriteAuditor), typeof(IAccountKeyRewrapper)
                 })
        {
            services.Single(descriptor => descriptor.ServiceType == serviceType)
                .Lifetime.ShouldBe(ServiceLifetime.Singleton, serviceType.Name);
        }
    }

    [Fact]
    public void AddLedgerSecurity_ApiHost_RegistersTheKeysHealthCheckAsDegradedAndReady()
    {
        using var provider = Build(Services(ConfigurationProviderValues(), ApiSources));

        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value
            .Registrations.Single(candidate => candidate.Name == "keys");

        registration.FailureStatus.ShouldBe(HealthStatus.Degraded);
        registration.Tags.ShouldContain(HealthCheckTags.Ready);
    }

    [Fact]
    public void AddLedgerSecurity_WorkerHost_DoesNotRegisterTheKeysHealthCheck()
    {
        using var provider = Build(Services(ConfigurationProviderValues(), WorkerSources));

        provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value
            .Registrations.ShouldNotContain(candidate => candidate.Name == "keys");
    }

    [Fact]
    public void AddLedgerSecurity_DirectoryProvider_BuildsTheDirectoryBackedProvider()
    {
        using var directory = new KeyDirectory().WithDefaultVersionOne();
        var values = new Dictionary<string, string?>
        {
            ["Security:Pii:Provider"] = "Directory",
            ["Security:Pii:Directory"] = directory.Root,
            ["Security:Pii:ActiveKeyVersion"] = "1"
        };

        using var provider = Build(Services(values, WorkerSources));

        provider.GetRequiredService<IKeyProvider>().ShouldBeOfType<ReloadingKeyProvider>();
        provider.GetRequiredService<IKeyProvider>().IsAvailable.ShouldBeTrue();
    }

    [Fact]
    public void AddLedgerSecurity_DirectoryProviderWithAnUnreachableDirectory_StillResolvesAndReportsUnavailable()
    {
        var values = new Dictionary<string, string?>
        {
            ["Security:Pii:Provider"] = "Directory",
            ["Security:Pii:Directory"] = Path.Combine(Path.GetTempPath(), "ledger-missing-" + Guid.NewGuid().ToString("N")),
            ["Security:Pii:ActiveKeyVersion"] = "1"
        };

        using var provider = Build(Services(values, ApiSources));

        provider.GetRequiredService<IKeyProvider>().IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public void AddLedgerSecurity_WorkerHost_ResolvesTheCursorProtectorOnlyWhenTheKeyExists()
    {
        var values = ConfigurationProviderValues();
        values.Remove("Security:Cursor:SigningKey");

        using var provider = Build(Services(values, WorkerSources));

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStatementCursorProtector>());
        provider.GetRequiredService<IKeyProvider>().IsAvailable.ShouldBeTrue();
    }

    [Fact]
    public void AddLedgerSecurity_ApiHostWithoutTheCursorKey_FailsOnStartValidation()
    {
        var values = ConfigurationProviderValues();
        values.Remove("Security:Cursor:SigningKey");
        using var provider = Build(Services(values, ApiSources));

        var failure = Should.Throw<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<CursorOptions>>().Value);

        failure.Message.ShouldContain("Security:Cursor:SigningKey", Case.Sensitive);
    }

    [Fact]
    public void AddLedgerSecurity_MalformedConfigurationKeys_FailOnOptionsValidation()
    {
        var values = ConfigurationProviderValues();
        values["Security:Pii:KeySets:1:EncryptionKey"] = Convert.ToBase64String(new byte[31]);
        using var provider = Build(Services(values, ApiSources));

        var failure = Should.Throw<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<PiiOptions>>().Value);

        failure.Message.ShouldContain("Security:Pii:KeySets:1:EncryptionKey", Case.Sensitive);
    }

    [Fact]
    public void AddLedgerSecurity_DeniedWriteSettings_BindFromTheSection()
    {
        var values = ConfigurationProviderValues();
        values["Security:Audit:DeniedWrite:Capacity"] = "4";
        values["Security:Audit:DeniedWrite:RefillPerSecond"] = "2";
        using var provider = Build(Services(values, ApiSources));

        var options = provider.GetRequiredService<IOptions<DeniedWriteAuditOptions>>().Value;

        options.Capacity.ShouldBe(4);
        options.RefillPerSecond.ShouldBe(2);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AddLedgerSecurity_AuditTrail_UsesTheWriteSourceInTheApiAndTheWorkerSourceInTheWorker(bool api)
    {
        var connections = new RefusingConnectionFactory();
        using var provider = Build(Services(
            ConfigurationProviderValues(),
            api ? ApiSources : WorkerSources,
            connections));
        var trail = provider.GetRequiredService<IAuditTrail>();
        var accountId = AccountId.From(Guid.Parse(SecurityVectors.AccountText)).Value;

        await Should.ThrowAsync<InvalidOperationException>(() =>
            trail.RecordAsync(AuditEvents.AccountCreated("client", accountId, "corr"), CancellationToken.None));

        connections.Sources.ShouldBe([api ? PostgresSource.Write : PostgresSource.Worker]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheFailureClassifier_IsTheCompositeOneWhicheverRegistrationComesFirst(bool classifierRegisteredBefore)
    {
        var services = Services(
            ConfigurationProviderValues(),
            ApiSources,
            classifierRegisteredBefore: classifierRegisteredBefore);

        services.Count(descriptor => descriptor.ServiceType == typeof(ITransientFailureClassifier)).ShouldBe(1);

        using var provider = Build(services);
        var classifier = provider.GetRequiredService<ITransientFailureClassifier>();

        classifier.ShouldBeOfType<CompositeTransientFailureClassifier>();
        classifier.IsTransient(new KeyProviderUnavailableException()).ShouldBeTrue();
        classifier.IsTransient(new TimeoutException()).ShouldBeTrue();
        classifier.IsTransient(new InvalidOperationException()).ShouldBeFalse();
    }

    [Fact]
    public void AddLedgerSecurity_RegistersTheKeyProviderGuardAsAHostedService()
    {
        var services = Services(ConfigurationProviderValues(), ApiSources);

        services.Any(descriptor =>
                descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(KeyProviderStartupGuard))
            .ShouldBeTrue();
    }

    private sealed class RefusingConnectionFactory : IPostgresConnectionFactory
    {
        private readonly List<PostgresSource> _sources = [];

        public IReadOnlyList<PostgresSource> Sources => _sources;

        public ValueTask<NpgsqlConnection> OpenConnectionAsync(PostgresSource source, CancellationToken cancellationToken)
        {
            _sources.Add(source);

            throw new InvalidOperationException("The test factory never opens a connection.");
        }
    }
}
