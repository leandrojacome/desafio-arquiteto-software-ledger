using System.Diagnostics.CodeAnalysis;
using Ledger.Infrastructure.Observability;
using Ledger.Infrastructure.Tests.Observability.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog.Context;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Ledger.Infrastructure.Tests.Observability;

[Trait("Category", "Unit")]
[SuppressMessage("Performance", "CA1848", Justification = "The tests log through the abstraction to exercise the real pipeline.")]
public sealed class LedgerLoggingTests
{
    private readonly CollectingLogSink _sink = new();

    private ServiceProvider Provider(
        string environment = "Testing",
        params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(pair => pair.Key, pair => (string?)pair.Value))
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(environment));
        services.AddSingleton<ILogEventSink>(_sink);
        services.AddLogging();
        services.AddLedgerLogging("ledger-api");

        return services.BuildServiceProvider();
    }

    private static ILogger<LedgerLoggingTests> LoggerOf(ServiceProvider provider) =>
        provider.GetRequiredService<ILogger<LedgerLoggingTests>>();

    [Fact]
    public void AddLedgerLogging_StampsEveryEventWithServiceVersionAndEnvironment()
    {
        using var provider = Provider("Staging");

        LoggerOf(provider).LogWarning("something happened");

        var logEvent = _sink.Events.Single();
        CollectingLogSink.Property(logEvent, "Service").ShouldBe("\"ledger-api\"");
        CollectingLogSink.Property(logEvent, "Environment").ShouldBe("\"Staging\"");
        CollectingLogSink.Property(logEvent, "Version").ShouldBe($"\"{BuildInfo.Version}\"");
    }

    [Fact]
    public void AddLedgerLogging_ReadsTheMinimumLevelFromTheConfiguration()
    {
        using var provider = Provider("Testing", ("Serilog:MinimumLevel:Default", "Warning"));
        var logger = LoggerOf(provider);

        logger.LogInformation("filtered out");
        logger.LogWarning("kept");

        _sink.Events.Select(logEvent => logEvent.Level).ShouldBe([LogEventLevel.Warning]);
    }

    [Fact]
    public void AddLedgerLogging_ReadsPerCategoryOverridesFromTheConfiguration()
    {
        using var provider = Provider(
            "Testing",
            ("Serilog:MinimumLevel:Default", "Warning"),
            ("Serilog:MinimumLevel:Override:Ledger.Audit", "Information"));
        var factory = provider.GetRequiredService<ILoggerFactory>();

        factory.CreateLogger("Ledger.Audit").LogInformation("audit kept");
        factory.CreateLogger("Ledger.Other").LogInformation("other dropped");

        _sink.Events.Count.ShouldBe(1);
        _sink.Events[0].MessageTemplate.Text.ShouldBe("audit kept");
    }

    [Fact]
    public void AddLedgerLogging_EventsInsideACorrelationScope_CarryTheCorrelationId()
    {
        using var provider = Provider();

        using (LogContext.PushProperty("CorrelationId", "9f3c1a7e2b4d4f60a1c8e5d7b3a29f10"))
        {
            LoggerOf(provider).LogWarning("inside the request");
        }

        CollectingLogSink.Property(_sink.Events.Single(), "CorrelationId").ShouldBe("\"9f3c1a7e2b4d4f60a1c8e5d7b3a29f10\"");
    }

    [Fact]
    public void AddLedgerLogging_RendersACompactJsonLineWithTheStructuredFields()
    {
        using var provider = Provider();

        LoggerOf(provider).LogWarning("Entry {EntryId} rejected for account {AccountId}", "e-1", "a-1");

        var json = _sink.Json(_sink.Events.Single()).TrimEnd();
        json.ShouldNotContain("\n");
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var root = document.RootElement;
        root.TryGetProperty("@i", out _).ShouldBeTrue();
        root.GetProperty("@m").GetString().ShouldBe("Entry \"e-1\" rejected for account \"a-1\"");
        root.GetProperty("@l").GetString().ShouldBe("Warning");
        root.TryGetProperty("@t", out _).ShouldBeTrue();
        root.GetProperty("EntryId").GetString().ShouldBe("e-1");
        root.GetProperty("AccountId").GetString().ShouldBe("a-1");
        root.GetProperty("Service").GetString().ShouldBe("ledger-api");
    }

    [Fact]
    public void AddLedgerLogging_ProtectsSensitiveTextInProperties()
    {
        using var provider = Provider();

        LoggerOf(provider).LogWarning(
            "Connection {Connection} refused for {Header}",
            "Host=db;Password=correct-horse",
            "Bearer abc.def.ghi");

        var json = _sink.AllJson();
        json.ShouldNotContain("correct-horse");
        json.ShouldNotContain("abc.def.ghi");
    }

    [Fact]
    public void AddLedgerLogging_DestructuredObjectsRespectTheSensitiveAttribute()
    {
        using var provider = Provider();

        LoggerOf(provider).LogWarning("Payload {@Payload}", new { Name = "n", HolderDocument = "12345678909" });

        _sink.AllJson().ShouldNotContain("12345678909");
    }

    [Fact]
    public void StartupLogger_WritesAtInformationAndAbove()
    {
        using var logger = StartupLogger.Create("ledger-worker");

        logger.IsEnabled(LogEventLevel.Information).ShouldBeTrue();
        logger.IsEnabled(LogEventLevel.Debug).ShouldBeFalse();
    }

    [Fact]
    public void StartupLogger_WithoutAServiceName_Throws()
    {
        Should.Throw<ArgumentException>(() => StartupLogger.Create(" "));
    }

    [Fact]
    public void RenderedCompactJsonFormatter_IsTheFormatUsedByTheConfiguration()
    {
        using var writer = new StringWriter();
        using var logger = new Serilog.LoggerConfiguration()
            .WithLedgerIdentity("ledger-worker", "Development")
            .WithLedgerEnrichers()
            .WithLedgerProtection()
            .WriteTo.Sink(_sink)
            .CreateLogger();

        logger.Information("hello {Name}", "world");
        new RenderedCompactJsonFormatter().Format(_sink.Events.Single(), writer);

        writer.ToString().ShouldContain("\"Service\":\"ledger-worker\"");
        writer.ToString().ShouldContain("\"Environment\":\"Development\"");
    }

    [Fact]
    public void AddLedgerLogging_DropsTheHostTraceOfAnInvalidConfiguration()
    {
        using var provider = Provider();
        var host = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Microsoft.Extensions.Hosting.Internal.Host");
        var failure = new OptionsValidationException("Postgres", typeof(object), ["Postgres:Host: is required."]);

        host.LogError(failure, "Hosting failed to start");
        host.LogError(new AggregateException(failure, failure), "Hosting failed to start");

        _sink.Events.ShouldBeEmpty();
    }

    [Fact]
    public void AddLedgerLogging_KeepsTheHostTraceOfAnyOtherFailure()
    {
        using var provider = Provider();
        var factory = provider.GetRequiredService<ILoggerFactory>();
        var failure = new OptionsValidationException("Postgres", typeof(object), ["Postgres:Host: is required."]);

        factory.CreateLogger("Microsoft.Extensions.Hosting.Internal.Host")
            .LogError(new InvalidOperationException("port already in use"), "Hosting failed to start");
        factory.CreateLogger("Microsoft.Extensions.Hosting.Internal.Host")
            .LogError(new AggregateException(failure, new InvalidOperationException("port already in use")), "Hosting failed to start");
        factory.CreateLogger("Ledger.Other").LogError(failure, "kept by another category");

        _sink.Events.Count.ShouldBe(3);
    }
}
