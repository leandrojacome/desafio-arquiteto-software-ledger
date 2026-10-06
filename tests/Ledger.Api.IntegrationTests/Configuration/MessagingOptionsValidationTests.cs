using Ledger.Infrastructure.Health;
using Ledger.Infrastructure.Messaging;
using Ledger.Infrastructure.Persistence.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Ledger.Api.IntegrationTests.Configuration;

[Trait("Category", "Unit")]
public sealed class MessagingOptionsValidationTests
{
    private const string Secret = "never-print-this-broker-password";

    [Fact]
    public void RabbitMq_WithValidSettings_Passes()
    {
        Validate(Rabbit(), Environments.Development).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("RabbitMq:Host", "", "RabbitMq")]
    [InlineData("RabbitMq:Port", "0", "RabbitMq")]
    [InlineData("RabbitMq:Port", "65536", "RabbitMq")]
    [InlineData("RabbitMq:Username", "", "RabbitMq")]
    [InlineData("RabbitMq:Password", "", "RabbitMq")]
    [InlineData("RabbitMq:Exchange", "", "RabbitMq")]
    [InlineData("RabbitMq:ConnectTimeoutSeconds", "0", "RabbitMq")]
    [InlineData("RabbitMq:ReconnectMinSeconds", "0", "RabbitMq")]
    [InlineData("RabbitMq:ReconnectMaxSeconds", "301", "RabbitMq")]
    [InlineData("RabbitMq:ReconnectJitterPercent", "51", "RabbitMq")]
    public void RabbitMq_WithAValueOutOfRange_IsRefusedAndTheMessageNamesTheSection(
        string key,
        string value,
        string expectedPrefix)
    {
        var result = Validate(Rabbit(new Dictionary<string, string?> { [key] = value }), Environments.Development);

        result.Failed.ShouldBeTrue();
        FailuresOf(result).ShouldAllBe(failure => failure.StartsWith(expectedPrefix, StringComparison.Ordinal));
    }

    [Fact]
    public void RabbitMq_Retention_DefaultsKeepAQuorumQueueForADayWithABoundedSize()
    {
        var options = Bind<RabbitMqOptions>(Rabbit(), RabbitMqOptions.SectionName);

        Validate(Rabbit(), Environments.Development).Succeeded.ShouldBeTrue();
        options.Retention.Enabled.ShouldBeTrue();
        options.Retention.Queue.ShouldBe("retention.ledger.entry-registered");
        options.Retention.DeadLetterQueue.ShouldBe("retention.ledger.entry-registered.dead-letter");
        options.Retention.DeadLetterExchange.ShouldBe("retention.ledger.entry-registered.dlx");
        options.Retention.TtlHours.ShouldBe(24);
        options.Retention.MaxLength.ShouldBe(1_000_000);
        options.Retention.MaxMegabytes.ShouldBe(1_024);
        options.Retention.DeadLetterTtlHours.ShouldBe(168);
        options.Retention.DeadLetterMaxLength.ShouldBe(100_000);
    }

    [Theory]
    [InlineData("RabbitMq:Retention:Queue", "")]
    [InlineData("RabbitMq:Retention:Queue", "ab")]
    [InlineData("RabbitMq:Retention:TtlHours", "0")]
    [InlineData("RabbitMq:Retention:TtlHours", "721")]
    [InlineData("RabbitMq:Retention:MaxLength", "999")]
    [InlineData("RabbitMq:Retention:MaxMegabytes", "0")]
    [InlineData("RabbitMq:Retention:DeadLetterTtlHours", "0")]
    [InlineData("RabbitMq:Retention:DeadLetterMaxLength", "10000001")]
    public void RabbitMq_Retention_WithAValueOutOfRange_IsRefusedAndTheMessageNamesTheSection(string key, string value)
    {
        var result = Validate(Rabbit(new Dictionary<string, string?> { [key] = value }), Environments.Development);

        result.Failed.ShouldBeTrue();
        FailuresOf(result).ShouldContain(failure => failure.StartsWith("RabbitMq:Retention", StringComparison.Ordinal));
    }

    [Fact]
    public void RabbitMq_Retention_WithAReservedQueueName_IsRefused()
    {
        var result = Validate(
            Rabbit(new Dictionary<string, string?> { ["RabbitMq:Retention:Queue"] = "amq.retention" }),
            Environments.Development);

        FailuresOf(result).ShouldContain(failure => failure.Contains("reserved", StringComparison.Ordinal));
    }

    [Fact]
    public void RabbitMq_Retention_Disabled_StillPassesAndKeepsTheOtherSettings()
    {
        var result = Validate(
            Rabbit(new Dictionary<string, string?> { ["RabbitMq:Retention:Enabled"] = "false" }),
            Environments.Development);

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void RabbitMq_WithMinimumAboveMaximum_IsRefused()
    {
        var result = Validate(
            Rabbit(new Dictionary<string, string?>
            {
                ["RabbitMq:ReconnectMinSeconds"] = "40",
                ["RabbitMq:ReconnectMaxSeconds"] = "30"
            }),
            Environments.Development);

        FailuresOf(result).ShouldContain(failure => failure.Contains("ReconnectMinSeconds", StringComparison.Ordinal));
    }

    [Fact]
    public void RabbitMq_InProductionWithoutTls_IsRefused()
    {
        var result = Validate(Rabbit(), Environments.Production);

        FailuresOf(result).ShouldContain(failure => failure.Contains("UseTls", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Homolog")]
    public void RabbitMq_InAnyEnvironmentButDevelopmentAndTesting_RequiresTls(string environment)
    {
        var result = Validate(Rabbit(), environment);

        FailuresOf(result).ShouldContain(failure => failure.Contains("UseTls", StringComparison.Ordinal));
    }

    [Fact]
    public void RabbitMq_InTestingWithoutTls_Passes()
    {
        Validate(Rabbit(), "Testing").Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void RabbitMq_InProductionWithTls_Passes()
    {
        var result = Validate(
            Rabbit(new Dictionary<string, string?> { ["RabbitMq:UseTls"] = "true" }),
            Environments.Production);

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void RabbitMq_FailureMessages_NeverCarryThePassword()
    {
        var result = Validate(
            Rabbit(new Dictionary<string, string?>
            {
                ["RabbitMq:Host"] = string.Empty,
                ["RabbitMq:Port"] = "0",
                ["RabbitMq:Password"] = Secret
            }),
            Environments.Production);

        result.Failed.ShouldBeTrue();
        string.Join(' ', FailuresOf(result)).ShouldNotContain(Secret);
    }

    [Fact]
    public void Outbox_WithDefaults_PassesAndBuildsTheSettings()
    {
        var options = Bind<OutboxOptions>(new Dictionary<string, string?>(), OutboxOptions.SectionName);

        new OutboxOptionsValidator().Validate(null, options).Succeeded.ShouldBeTrue();

        var settings = options.ToSettings();

        settings.BatchSize.ShouldBe(200);
        settings.Lease.ShouldBe(TimeSpan.FromSeconds(30));
        settings.ConfirmTimeout.ShouldBe(TimeSpan.FromSeconds(5));
        settings.FailedAttempts.ShouldBe(5);
        settings.FailedHeadWindow.ShouldBe(1000);
        settings.PendingCap.ShouldBe(1_000_000);
        settings.Retention.ShouldBe(TimeSpan.FromDays(7));
        settings.PruneBatchSize.ShouldBe(5000);
    }

    [Theory]
    [InlineData("Outbox:BatchSize", "0")]
    [InlineData("Outbox:BatchSize", "1001")]
    [InlineData("Outbox:IdlePollMs", "9")]
    [InlineData("Outbox:IdlePollMs", "5001")]
    [InlineData("Outbox:LeaseSeconds", "4")]
    [InlineData("Outbox:LeaseSeconds", "601")]
    [InlineData("Outbox:ConfirmTimeoutSeconds", "0")]
    [InlineData("Outbox:ConfirmTimeoutSeconds", "61")]
    [InlineData("Outbox:RetentionDays", "0")]
    [InlineData("Outbox:RetentionDays", "366")]
    [InlineData("Outbox:PruneIntervalMinutes", "0")]
    [InlineData("Outbox:PruneBatchSize", "99")]
    [InlineData("Outbox:MeasureIntervalSeconds", "301")]
    [InlineData("Outbox:PendingCap", "999")]
    [InlineData("Outbox:FailedAttempts", "1")]
    [InlineData("Outbox:FailedHeadWindow", "9")]
    public void Outbox_WithAValueOutOfRange_IsRefusedAndTheMessageNamesTheKey(string key, string value)
    {
        var options = Bind<OutboxOptions>(new Dictionary<string, string?> { [key] = value }, OutboxOptions.SectionName);

        var result = new OutboxOptionsValidator().Validate(null, options);

        result.Failed.ShouldBeTrue();
        FailuresOf(result).ShouldAllBe(failure => failure.StartsWith("Outbox", StringComparison.Ordinal));
    }

    [Fact]
    public void Outbox_WithTheLeaseNotAboveTheConfirmationTimeout_IsRefused()
    {
        var options = Bind<OutboxOptions>(
            new Dictionary<string, string?>
            {
                ["Outbox:LeaseSeconds"] = "10",
                ["Outbox:ConfirmTimeoutSeconds"] = "10"
            },
            OutboxOptions.SectionName);

        var result = new OutboxOptionsValidator().Validate(null, options);

        FailuresOf(result).ShouldContain(failure => failure.Contains("LeaseSeconds", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Resilience:BrokerCircuitBreaker:FailureRatio", "0.05")]
    [InlineData("Resilience:BrokerCircuitBreaker:FailureRatio", "1.5")]
    [InlineData("Resilience:BrokerCircuitBreaker:SamplingSeconds", "4")]
    [InlineData("Resilience:BrokerCircuitBreaker:MinimumThroughput", "1")]
    [InlineData("Resilience:BrokerCircuitBreaker:BreakSeconds", "4")]
    public void BrokerCircuit_WithAValueOutOfRange_IsRefused(string key, string value)
    {
        var options = Bind<BrokerCircuitOptions>(
            new Dictionary<string, string?> { [key] = value },
            BrokerCircuitOptions.SectionName);

        new BrokerCircuitOptionsValidator().Validate(null, options).Failed.ShouldBeTrue();
    }

    [Fact]
    public void BrokerCircuit_WithDefaults_MatchesTheContract()
    {
        var options = Bind<BrokerCircuitOptions>(new Dictionary<string, string?>(), BrokerCircuitOptions.SectionName);

        new BrokerCircuitOptionsValidator().Validate(null, options).Succeeded.ShouldBeTrue();
        options.FailureRatio.ShouldBe(0.5);
        options.SamplingSeconds.ShouldBe(30);
        options.MinimumThroughput.ShouldBe(10);
        options.BreakSeconds.ShouldBe(30);
    }

    [Theory]
    [InlineData("Resilience:Health:OutboxHeartbeatSeconds", "9")]
    [InlineData("Resilience:Health:OutboxHeartbeatSeconds", "3601")]
    [InlineData("Resilience:Health:IntegrityHeartbeatMinutes", "0")]
    [InlineData("Resilience:Health:IntegrityHeartbeatMinutes", "1441")]
    [InlineData("Resilience:Health:OutboxLagSeconds", "4")]
    public void WorkerHealth_WithAValueOutOfRange_IsRefused(string key, string value)
    {
        var options = Bind<WorkerHealthOptions>(
            new Dictionary<string, string?> { [key] = value },
            WorkerHealthOptions.SectionName);

        new WorkerHealthOptionsValidator().Validate(null, options).Failed.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Idempotency:RetentionDays", "0")]
    [InlineData("Idempotency:RetentionDays", "366")]
    [InlineData("Idempotency:PruneIntervalMinutes", "0")]
    [InlineData("Idempotency:PruneBatchSize", "99")]
    public void IdempotencyPrune_WithAValueOutOfRange_IsRefused(string key, string value)
    {
        var options = Bind<IdempotencyPruneOptions>(
            new Dictionary<string, string?> { [key] = value },
            IdempotencyPruneOptions.SectionName);

        new IdempotencyPruneOptionsValidator().Validate(null, options).Failed.ShouldBeTrue();
    }

    [Fact]
    public void IdempotencyPrune_WithDefaults_KeepsKeysForThirtyFiveDays()
    {
        var options = Bind<IdempotencyPruneOptions>(new Dictionary<string, string?>(), IdempotencyPruneOptions.SectionName);

        new IdempotencyPruneOptionsValidator().Validate(null, options).Succeeded.ShouldBeTrue();
        options.RetentionDays.ShouldBe(35);
        options.PruneIntervalMinutes.ShouldBe(10);
        options.PruneBatchSize.ShouldBe(5000);
    }

    [Fact]
    public void OutboxSettings_RefusesALeaseThatDoesNotOutliveTheConfirmationTimeout()
    {
        var options = new OutboxOptions { LeaseSeconds = 5, ConfirmTimeoutSeconds = 5 };

        Should.Throw<ArgumentOutOfRangeException>(() => options.ToSettings());
    }

    private static IReadOnlyList<string> FailuresOf(ValidateOptionsResult result) => [.. result.Failures ?? []];

    private static Dictionary<string, string?> Rabbit(IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["RabbitMq:Host"] = "broker.test",
            ["RabbitMq:Port"] = "5672",
            ["RabbitMq:Username"] = "ledger_worker",
            ["RabbitMq:Password"] = "a-password",
            ["RabbitMq:Exchange"] = "ledger.events"
        };

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                values[key] = value;
            }
        }

        return values;
    }

    private static ValidateOptionsResult Validate(Dictionary<string, string?> values, string environment)
    {
        var options = Bind<RabbitMqOptions>(values, RabbitMqOptions.SectionName);

        return new RabbitMqOptionsValidator(new StubEnvironment(environment)).Validate(null, options);
    }

    private static TOptions Bind<TOptions>(IReadOnlyDictionary<string, string?> values, string section)
        where TOptions : class, new()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build()
            .GetSection(section)
            .Get<TOptions>() ?? new TOptions();
    }

    private sealed class StubEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
