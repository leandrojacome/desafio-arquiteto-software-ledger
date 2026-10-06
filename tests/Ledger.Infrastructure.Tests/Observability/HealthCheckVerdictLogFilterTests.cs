using System.Diagnostics.CodeAnalysis;
using Ledger.Infrastructure.Observability;
using Ledger.Infrastructure.Tests.Observability.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;

namespace Ledger.Infrastructure.Tests.Observability;

[Trait("Category", "Unit")]
[SuppressMessage("Performance", "CA1848", Justification = "The tests log through the abstraction to exercise the real pipeline.")]
public sealed class HealthCheckVerdictLogFilterTests
{
    private const string FrameworkCategory = "Microsoft.Extensions.Diagnostics.HealthChecks.DefaultHealthCheckService";
    private const string VerdictTemplate = "Health check {HealthCheckName} with status {HealthStatus} completed after {ElapsedMilliseconds}ms with message '{Description}'";

    private readonly CollectingLogSink _sink = new();

    private ServiceProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment("Testing"));
        services.AddSingleton<ILogEventSink>(_sink);
        services.AddLogging();
        services.AddLedgerLogging("ledger-api");

        return services.BuildServiceProvider();
    }

    private static void Verdict(ServiceProvider provider, string category, int eventId, string check, LogLevel level)
    {
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(category);
        var id = new EventId(eventId, "HealthCheckEnd");
        object[] arguments = [check, "Unhealthy", 12.5, "x"];

        logger.Log(level, id, VerdictTemplate, arguments);
    }

    [Theory]
    [InlineData("postgres")]
    [InlineData("schema")]
    [InlineData("rabbitmq")]
    [InlineData("broker-circuit")]
    [InlineData("shutdown")]
    public void TheFrameworkVerdict_OfACheckThatLogsItsOwnEvent_IsDropped(string check)
    {
        using var provider = Provider();

        Verdict(provider, FrameworkCategory, 103, check, LogLevel.Error);

        _sink.Events.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("outbox-lag")]
    [InlineData("key-usage")]
    [InlineData("keys")]
    [InlineData("heartbeats")]
    public void TheFrameworkVerdict_OfACheckWithNoEventOfItsOwn_IsKept(string check)
    {
        using var provider = Provider();

        Verdict(provider, FrameworkCategory, 103, check, LogLevel.Warning);

        _sink.Events.Count.ShouldBe(1);
        _sink.Events[0].Level.ShouldBe(LogEventLevel.Warning);
    }

    [Fact]
    public void AnUnhandledExceptionOfACheck_IsKeptEvenForACheckThatLogsItsOwnEvent()
    {
        using var provider = Provider();

        Verdict(provider, FrameworkCategory, 104, "postgres", LogLevel.Error);

        _sink.Events.Count.ShouldBe(1);
    }

    [Fact]
    public void TheSameEventIdFromAnotherCategory_IsKept()
    {
        using var provider = Provider();

        Verdict(provider, "Ledger.Other", 103, "postgres", LogLevel.Error);

        _sink.Events.Count.ShouldBe(1);
    }
}
