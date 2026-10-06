extern alias LedgerWorker;

using System.Globalization;
using System.Reflection;
using Ledger.Api.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Configuration;

[Collection(MessagingCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class WorkerStartupTests(PostgresFixture postgres)
{
    public static TheoryData<string, string> InvalidSettings => new()
    {
        { "RabbitMq:Host", string.Empty },
        { "RabbitMq:Password", string.Empty },
        { "RabbitMq:Port", "70000" },
        { "Outbox:BatchSize", "0" },
        { "Outbox:LeaseSeconds", "2" },
        { "Integrity:RecentIntervalMinutes", "61" },
        { "Integrity:HeadBatchSize", "99" },
        { "Idempotency:RetentionDays", "0" },
        { "Resilience:BrokerCircuitBreaker:FailureRatio", "5" },
        { "Resilience:Health:OutboxHeartbeatSeconds", "1" }
    };

    [Theory]
    [MemberData(nameof(InvalidSettings))]
    public async Task AWorkerWithAnInvalidSetting_ExitsWithCode3WithoutStartingAnyService(string key, string value)
    {
        var settings = BaseSettings();

        settings[key] = value;

        var arguments = new List<string> { "--environment=Testing" };

        arguments.AddRange(settings.Select(pair => $"--{pair.Key}={pair.Value}"));

        var exitCode = await RunEntryPointAsync([.. arguments]).WaitAsync(TimeSpan.FromSeconds(60));

        exitCode.ShouldBe(3);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Homolog")]
    public async Task AWorkerInAStrictEnvironmentWithTheDevelopmentSettings_ExitsWithCode3NamingTheBrokenRules(
        string environment)
    {
        var arguments = new List<string> { $"--environment={environment}" };

        arguments.AddRange(BaseSettings().Select(pair => $"--{pair.Key}={pair.Value}"));

        using var output = new StringWriter();
        var original = Console.Out;

        Console.SetOut(output);

        int exitCode;

        try
        {
            exitCode = await RunEntryPointAsync([.. arguments]).WaitAsync(TimeSpan.FromSeconds(60));
        }
        finally
        {
            Console.SetOut(original);
        }

        exitCode.ShouldBe(3);
        output.ToString().ShouldContain("Postgres:SslMode");
        output.ToString().ShouldContain("VerifyFull");
        output.ToString().ShouldNotContain("Hosting failed to start");
    }

    [DockerFact]
    public async Task TheInspectCommand_OnAHealthyAccount_ExitsWithCode0AndStartsNoService()
    {
        await using var database = await postgres.CreateEmptyDatabaseAsync(CancellationToken.None);
        (await database.MigrateAsync(CancellationToken.None)).Succeeded.ShouldBeTrue();
        var account = await new IntegrityLedger(database).SeedAsync();

        var exitCode = await RunInspectionAsync(database, account.AccountId.ToString());

        exitCode.ShouldBe(0);
    }

    [DockerFact]
    public async Task TheInspectCommand_OnAnAccountWhoseStoredBalanceWasTamperedWith_ExitsWithCode1()
    {
        await using var database = await postgres.CreateEmptyDatabaseAsync(CancellationToken.None);
        (await database.MigrateAsync(CancellationToken.None)).Succeeded.ShouldBeTrue();
        var ledger = new IntegrityLedger(database);
        var account = await ledger.SeedAsync();

        await ledger.TamperStoredBalanceAsync(account.AccountId, 5.00m);

        var exitCode = await RunInspectionAsync(database, account.AccountId.ToString());

        exitCode.ShouldBe(1);
    }

    [DockerTheory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    [InlineData(null)]
    public async Task TheInspectCommand_WithoutAValidAccountId_ExitsWithCode3(string? account)
    {
        await using var database = await postgres.CreateEmptyDatabaseAsync(CancellationToken.None);

        var exitCode = await RunInspectionAsync(database, account);

        exitCode.ShouldBe(3);
    }

    [Fact]
    public void TheWorkerHost_InDevelopment_BuildsWithEveryRegistrationValidated()
    {
        using var factory = new MessagingWorkerFactory(BaseSettings(), environmentName: "Development");

        var services = factory.Services;

        services.GetRequiredService<Ledger.Application.Abstractions.IEventPublisher>().ShouldNotBeNull();
        services.GetRequiredService<Ledger.Application.Abstractions.IOutboxQueue>().ShouldNotBeNull();
        services.GetRequiredService<Ledger.Application.Abstractions.IWorkerHeartbeat>().ShouldNotBeNull();
        services.GetRequiredService<Ledger.Application.Integrity.IIntegritySessions>().ShouldNotBeNull();
        services.GetRequiredService<Ledger.Application.Outbox.OutboxSettings>().BatchSize.ShouldBe(200);
    }

    [Fact]
    public void TheWorkerHost_TakesTheShutdownTimeoutFromTheResilienceOptions()
    {
        var settings = BaseSettings();

        settings["Resilience:ShutdownTimeoutSeconds"] = "12";

        using var factory = new MessagingWorkerFactory(settings);

        factory.Services.GetRequiredService<IOptions<HostOptions>>().Value.ShutdownTimeout
            .ShouldBe(TimeSpan.FromSeconds(12));
    }

    [Fact]
    public void TheWorkerHost_RegistersTheMessagingPortsAsSingletons()
    {
        using var factory = new MessagingWorkerFactory(BaseSettings());

        var services = factory.Services;

        services.GetRequiredService<Ledger.Application.Abstractions.IEventPublisher>()
            .ShouldBeSameAs(services.GetRequiredService<Ledger.Application.Abstractions.IEventPublisher>());
        services.GetRequiredService<Ledger.Application.Abstractions.IOutboxQueue>()
            .ShouldBeSameAs(services.GetRequiredService<Ledger.Application.Abstractions.IOutboxQueue>());
        services.GetRequiredService<Ledger.Application.Abstractions.IWorkerHeartbeat>()
            .ShouldBeSameAs(services.GetRequiredService<Ledger.Application.Abstractions.IWorkerHeartbeat>());
    }

    [DockerFact]
    public async Task TheMigrateCommand_RunsWithoutAnyBrokerConfigurationAndInDevelopment_AndAppliesTheSchema()
    {
        await using var database = await postgres.CreateEmptyDatabaseAsync(CancellationToken.None);

        var settings = postgres.ConfigurationWith(new Dictionary<string, string?> { ["Postgres:Database"] = database.Name });
        var arguments = new List<string> { "--environment=Development" };

        arguments.AddRange(settings
            .Where(pair => pair.Key.StartsWith("Postgres:", StringComparison.Ordinal) && pair.Value is not null)
            .Select(pair => $"--{pair.Key}={pair.Value}"));

        arguments.Add("--migrate");

        var exitCode = await RunEntryPointAsync([.. arguments]);

        exitCode.ShouldBe(0);

        await using var connection = await database.OpenAdministrativeConnectionAsync(CancellationToken.None);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM schemaversions", connection);

        Convert.ToInt64(await command.ExecuteScalarAsync(CancellationToken.None), CultureInfo.InvariantCulture)
            .ShouldBeGreaterThanOrEqualTo(3);
        (await database.MigrateAsync(CancellationToken.None)).AppliedScripts.ShouldBeEmpty();
    }

    private async Task<int> RunInspectionAsync(EmptyDatabase database, string? account)
    {
        var settings = postgres.ConfigurationWith(new Dictionary<string, string?> { ["Postgres:Database"] = database.Name });
        var arguments = new List<string> { "--environment=Testing" };

        arguments.AddRange(settings
            .Where(pair => pair.Key.StartsWith("Postgres:", StringComparison.Ordinal) && pair.Value is not null)
            .Select(pair => $"--{pair.Key}={pair.Value}"));

        arguments.Add(account is null ? "--inspect-account" : $"--inspect-account={account}");

        return await RunEntryPointAsync([.. arguments]).WaitAsync(TimeSpan.FromSeconds(60));
    }

    private static Dictionary<string, string?> BaseSettings()
    {
        var settings = TestConfiguration.ForUnreachablePostgres();

        settings["RabbitMq:Host"] = "127.0.0.1";
        settings["RabbitMq:Port"] = "1";
        settings["RabbitMq:Username"] = "ledger_worker";
        settings["RabbitMq:Password"] = "a-broker-password";

        return settings;
    }

    private static async Task<int> RunEntryPointAsync(string[] arguments)
    {
        var entryPoint = typeof(LedgerWorker::Program).Assembly.EntryPoint ??
                         throw new InvalidOperationException("The worker assembly has no entry point.");

        var invoked = entryPoint.Invoke(null, [arguments]);

        return invoked switch
        {
            Task<int> task => await task,
            int code => code,
            _ => throw new InvalidOperationException("The worker entry point returned an unexpected value.")
        };
    }
}
