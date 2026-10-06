using System.Net;
using System.Text.Json;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application;
using Ledger.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Ledger.Api.IntegrationTests.Configuration;

[Collection(MessagingCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class HostCompositionTests(PostgresFixture postgres, RabbitMqFixture broker)
{
    private const string AuthorityHost = "Testing";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private static readonly HashSet<Type> NotResolvedFromTheContainer =
    [
        typeof(IAccountCreationOperation),
        typeof(IEntryOperation),
        typeof(IOutboxPollOperation),
        typeof(IOutboxPublishOperation),
        typeof(IRewrapBatchOperation),
        typeof(IStatementOperation),
        typeof(IAccountRepository),
        typeof(IEntryRepository),
        typeof(IIdempotencyStore),
        typeof(IOutbox),
        typeof(IUnitOfWorkScope),
        typeof(Ledger.Application.Integrity.IIntegritySession),
        typeof(Ledger.Application.Integrity.IIntegrityRunTelemetry)
    ];

    public static TheoryData<string> Environments => new() { "Development", AuthorityHost };

    public static TheoryData<string> StrictEnvironments => new() { "Production", "Staging", "Homolog" };

    [DockerTheory]
    [MemberData(nameof(Environments))]
    public async Task TheApiHost_WithEveryRegistrationValidated_ResolvesEveryPortAndReportsReady(string environment)
    {
        using var publicKey = TemporaryPublicKey.Create();
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        await using var factory = new ValidatedApiFactory(ApiSettings(scenario, environment, publicKey), environment);

        AssertEveryPortAndHandlerResolves(factory.Services);

        using var client = factory.CreateClient();
        using var live = await client.GetAsync("/health/live", CancellationToken.None);
        using var ready = await client.GetAsync("/health/ready", CancellationToken.None);

        live.StatusCode.ShouldBe(HttpStatusCode.OK);
        ready.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StatusOf(ready)).ShouldBe("Healthy");
    }

    [DockerTheory]
    [MemberData(nameof(Environments))]
    public async Task TheWorkerHost_WithEveryRegistrationValidated_ResolvesEveryPortAndReportsReady(string environment)
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        await using var factory = new ValidatedWorkerFactory(scenario.Settings(), environment);

        AssertEveryPortAndHandlerResolves(factory.Services);

        using var client = factory.CreateClient();

        await ConditionWait.UntilAsync(
            async () =>
            {
                using var response = await client.GetAsync("/health/ready", CancellationToken.None);

                return response.StatusCode == HttpStatusCode.OK && await StatusOf(response) == "Healthy";
            },
            Patience,
            "the worker reporting ready with the database and the broker up");

        using var live = await client.GetAsync("/health/live", CancellationToken.None);

        live.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StatusOf(live)).ShouldBe("Healthy");
    }

    [DockerTheory]
    [MemberData(nameof(StrictEnvironments))]
    public async Task TheApiHost_InAStrictEnvironment_AppliesTheProductionRulesAndFailsClosedWithoutVerifiedTls(
        string environment)
    {
        using var keys = TemporaryKeyDirectory.WithVersionOne();
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        await using var factory = new ValidatedApiFactory(
            ProductionRules(ApiSettings(scenario, AuthorityHost, null), keys),
            environment);

        AssertEveryPortAndHandlerResolves(factory.Services);

        using var client = factory.CreateClient();
        using var live = await client.GetAsync("/health/live", CancellationToken.None);
        using var ready = await client.GetAsync("/health/ready", CancellationToken.None);

        live.StatusCode.ShouldBe(HttpStatusCode.OK);
        ready.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await StatusOf(ready)).ShouldBe("Unhealthy");
    }

    [DockerTheory]
    [MemberData(nameof(StrictEnvironments))]
    public async Task TheWorkerHost_InAStrictEnvironment_AppliesTheProductionRulesAndFailsClosedWithoutVerifiedTls(
        string environment)
    {
        using var keys = TemporaryKeyDirectory.WithVersionOne();
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        await using var factory = new ValidatedWorkerFactory(ProductionRules(scenario.Settings(), keys), environment);

        AssertEveryPortAndHandlerResolves(factory.Services);

        using var client = factory.CreateClient();
        using var live = await client.GetAsync("/health/live", CancellationToken.None);
        using var ready = await client.GetAsync("/health/ready", CancellationToken.None);

        live.StatusCode.ShouldBe(HttpStatusCode.OK);
        ready.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await StatusOf(ready)).ShouldBe("Unhealthy");
    }

    [DockerTheory]
    [MemberData(nameof(StrictEnvironments))]
    public async Task TheApiHost_InAStrictEnvironmentWithTheDevelopmentSettings_RefusesToStartNamingEachRuleItBroke(
        string environment)
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);

        var failures = await RefusalFailuresAsync(
            () => new ValidatedApiFactory(scenario.Settings(), environment),
            factory => factory.Services);

        failures.ShouldContain(failure => failure.StartsWith("Postgres:SslMode", StringComparison.Ordinal));
    }

    [DockerTheory]
    [MemberData(nameof(StrictEnvironments))]
    public async Task TheApiHost_InAStrictEnvironmentWithHttpMetadataAndTheLocalKey_RefusesToStart(string environment)
    {
        using var keys = TemporaryKeyDirectory.WithVersionOne();
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        var settings = ProductionRules(scenario.Settings(), keys);

        var failures = await RefusalFailuresAsync(
            () => new ValidatedApiFactory(settings, environment),
            factory => factory.Services);

        failures.ShouldContain(failure => failure.StartsWith("Authentication:Mode LocalKey", StringComparison.Ordinal));
        failures.ShouldNotContain(failure => failure.StartsWith("Postgres", StringComparison.Ordinal));
    }

    [DockerTheory]
    [MemberData(nameof(StrictEnvironments))]
    public async Task TheApiHost_InAStrictEnvironmentWithTheConfigurationKeyProvider_RefusesToStart(string environment)
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        var settings = ProductionRules(ApiSettings(scenario, AuthorityHost, null), TemporaryKeyDirectory.WithVersionOne());
        settings["Security:Pii:Provider"] = "Configuration";
        await using var factory = new ValidatedApiFactory(settings, environment);

        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => StartAsync(factory));

        refusal.Message.ShouldContain("Security:Pii:Provider");
    }

    [DockerFact]
    public async Task TheApiHost_WithoutAPortImplementation_RefusesToStartNamingTheMissingService()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        await using var factory = new ValidatedApiFactory(
            ApiSettings(scenario, AuthorityHost, null),
            AuthorityHost,
            services => services.RemoveAll<IKeyProvider>());

        var refusal = Should.Throw<AggregateException>(() => factory.Services);

        refusal.Message.ShouldContain(nameof(IKeyProvider));
    }

    [DockerFact]
    public async Task TheWorkerHost_WithoutAPortImplementation_RefusesToStartNamingTheMissingService()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        await using var factory = new ValidatedWorkerFactory(
            scenario.Settings(),
            AuthorityHost,
            services => services.RemoveAll<IOutboxQueue>());

        var refusal = Should.Throw<AggregateException>(() => factory.Services);

        refusal.Message.ShouldContain(nameof(IOutboxQueue));
    }

    private static async Task<List<string>> RefusalFailuresAsync<TFactory>(
        Func<TFactory> create,
        Func<TFactory, object?> start)
        where TFactory : IAsyncDisposable
    {
        var factory = create();

        try
        {
            return [.. Flatten(Should.Throw<Exception>(() => start(factory)))];
        }
        finally
        {
            await DisposeAfterFailedStartAsync(factory);
        }
    }

    private static async Task DisposeAfterFailedStartAsync(IAsyncDisposable factory)
    {
        try
        {
            await factory.DisposeAsync();
        }
        catch (ArgumentNullException)
        {
            return;
        }
    }

    private static IEnumerable<string> Flatten(Exception exception)
    {
        return exception switch
        {
            OptionsValidationException validation => validation.Failures,
            AggregateException aggregate => aggregate.InnerExceptions.SelectMany(Flatten),
            _ => throw new InvalidOperationException(
                $"The host refused to start for a reason other than an options validation: {exception.GetType().Name}.")
        };
    }

    private static Task StartAsync(ValidatedApiFactory factory)
    {
        _ = factory.Services;

        return Task.CompletedTask;
    }

    private static Dictionary<string, string?> ApiSettings(
        WorkerScenario scenario,
        string environment,
        TemporaryPublicKey? publicKey)
    {
        var settings = scenario.Settings();

        if (environment == "Development")
        {
            settings["Authentication:LocalKey:PublicKeyPath"] = publicKey?.Path
                ?? throw new InvalidOperationException("The development host needs a public key.");

            return settings;
        }

        settings["Authentication:Mode"] = "Authority";
        settings["Authentication:Authority"] = "https://auth.bank.internal";
        settings["Authentication:RequireHttpsMetadata"] = "true";
        settings["RateLimiting:Enabled"] = "true";

        return settings;
    }

    private static Dictionary<string, string?> ProductionRules(Dictionary<string, string?> settings, TemporaryKeyDirectory keys)
    {
        settings["Postgres:SslMode"] = "VerifyFull";
        settings["Postgres:IncludeErrorDetail"] = "false";
        settings["Security:Pii:Provider"] = "Directory";
        settings["Security:Pii:Directory"] = keys.Root;
        settings["RabbitMq:UseTls"] = "true";
        settings["Authorization:AccountProvisioningClients:0"] = "billing-core";

        return settings;
    }

    private static void AssertEveryPortAndHandlerResolves(IServiceProvider services)
    {
        var isService = services.GetRequiredService<IServiceProviderIsService>();
        var exported = ApplicationAssembly.Reference.GetExportedTypes();
        var ports = exported
            .Where(type => type.IsInterface && !NotResolvedFromTheContainer.Contains(type))
            .ToList();
        var handlers = exported
            .Where(type => type is { IsClass: true, IsSealed: true } && type.Name.EndsWith("Handler", StringComparison.Ordinal))
            .ToList();

        ports.ShouldNotBeEmpty();
        handlers.Count.ShouldBeGreaterThanOrEqualTo(11);

        var unregistered = ports.Concat(handlers).Where(type => !isService.IsService(type)).Select(type => type.Name);

        unregistered.ShouldBeEmpty();

        using var scope = services.CreateScope();

        foreach (var type in ports.Concat(handlers))
        {
            scope.ServiceProvider.GetRequiredService(type).ShouldNotBeNull();
        }
    }

    private static async Task<string?> StatusOf(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        using var document = JsonDocument.Parse(body);

        return document.RootElement.GetProperty("status").GetString();
    }
}
