using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Infrastructure.Messaging;
using Ledger.Infrastructure.Resilience;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using RabbitMQ.Client.Exceptions;

namespace Ledger.Api.IntegrationTests.Health;

[Trait("Category", "Unit")]
public sealed class RabbitMqReadinessHealthCheckTests
{
    private const string Secret = "do-not-print-this-broker-secret";

    private readonly FakeTimeProvider _time = new();
    private readonly LogCapture<RabbitMqReadinessHealthCheck> _log = new();
    private readonly ScriptedConnector _connector = new();

    [Fact]
    public async Task WhenTheBrokerAcceptsAConnection_IsHealthy()
    {
        var result = await CreateCheck().CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        result.Status.ShouldBe(HealthStatus.Healthy);
        _log.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenTheBrokerRefusesTheConnection_IsDegradedNeverUnhealthyAndLogsEvent9103()
    {
        _connector.Behavior = _ => throw new BrokerUnreachableException(new IOException(Secret));

        var result = await CreateCheck().CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        var logged = _log.Events.ShouldHaveSingleItem();

        result.Status.ShouldBe(HealthStatus.Degraded);
        logged.Id.ShouldBe(9103);
        logged.Name.ShouldBe("BrokerProbeDegraded");
        logged.Level.ShouldBe(LogLevel.Warning);
        logged.Message.ShouldNotContain(Secret);
        (result.Description ?? string.Empty).ShouldNotContain(Secret);
    }

    [Fact]
    public async Task WhenTheConnectionTakesLongerThanTheConnectTimeout_IsDegraded()
    {
        _connector.Behavior = token => Task.Delay(Timeout.InfiniteTimeSpan, token);

        var pending = CreateCheck().CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        _time.Advance(TimeSpan.FromSeconds(3));

        (await pending).Status.ShouldBe(HealthStatus.Degraded);
    }

    [Fact]
    public async Task WhenTheCallerCancels_TheCancellationPropagates()
    {
        _connector.Behavior = token => Task.Delay(Timeout.InfiniteTimeSpan, token);

        using var caller = new CancellationTokenSource();

        var pending = CreateCheck().CheckHealthAsync(new HealthCheckContext(), caller.Token);

        await caller.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task TheCachedProbe_ConnectsOncePerWindowNoMatterHowManyCallersAsk()
    {
        var cached = new CachedRabbitMqReadiness(CreateCheck(), Options.Create(new ResilienceOptions()), _time);
        var connections = 0;

        _connector.Behavior = async _ =>
        {
            Interlocked.Increment(ref connections);

            await Task.Yield();
        };

        var results = await Task.WhenAll(Enumerable.Range(0, 50)
            .Select(_ => cached.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None)));

        results.ShouldAllBe(result => result.Status == HealthStatus.Healthy);
        connections.ShouldBe(1);

        await cached.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        connections.ShouldBe(1);

        _time.Advance(TimeSpan.FromSeconds(6));

        await cached.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        connections.ShouldBe(2);
    }

    private RabbitMqReadinessHealthCheck CreateCheck()
    {
        var options = Options.Create(new RabbitMqOptions
        {
            Host = "broker.test",
            Username = "user",
            Password = Secret,
            ConnectTimeoutSeconds = 2
        });

        return new RabbitMqReadinessHealthCheck(_connector, options, _time, _log);
    }

    private sealed class ScriptedConnector : IRabbitMqConnector
    {
        public Func<CancellationToken, Task> Behavior { get; set; } = _ => Task.CompletedTask;

        public Task ConnectAsync(CancellationToken cancellationToken) => Behavior(cancellationToken);
    }
}
