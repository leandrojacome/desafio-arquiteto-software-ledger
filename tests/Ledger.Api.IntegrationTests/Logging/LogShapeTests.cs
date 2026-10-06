using System.Text.Json;
using System.Text.RegularExpressions;
using Ledger.Api.IntegrationTests.Observability;
using Ledger.Infrastructure.Observability;
using Serilog.Events;

namespace Ledger.Api.IntegrationTests.Logging;

[Trait("Category", "Integration")]
[Collection(OtelEnvironment.Collection)]
public sealed partial class LogShapeTests
{
    private const string CorrelationHeader = "X-Correlation-Id";

    private static async Task<(CapturingLogSink Sink, string Correlation)> RequestAsync(string route)
    {
        using var clean = OtelEnvironment.Clean();
        var sink = new CapturingLogSink();
        using var factory = new ObservabilityApiFactory(sink);
        var correlation = Guid.NewGuid().ToString("N");
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.Add(CorrelationHeader, correlation);

        using var response = await client.SendAsync(request, CancellationToken.None);

        return (sink, correlation);
    }

    private static LogEvent Summary(CapturingLogSink sink, string correlation) =>
        sink.Events.Single(candidate =>
            candidate.MessageTemplate.Text.StartsWith("HTTP ", StringComparison.Ordinal)
            && CapturingLogSink.Property(candidate, "CorrelationId") == $"\"{correlation}\"");

    [Fact]
    public async Task RequestSummary_CarriesTheIdentityTheTraceTheRouteAndTheStatus()
    {
        var (sink, correlation) = await RequestAsync("/health/live");

        var summary = Summary(sink, correlation);

        CapturingLogSink.Property(summary, "Service").ShouldBe("\"ledger-api\"");
        CapturingLogSink.Property(summary, "Version").ShouldBe($"\"{BuildInfo.Version}\"");
        CapturingLogSink.Property(summary, "Environment").ShouldBe("\"Testing\"");
        CapturingLogSink.Property(summary, "CorrelationId").ShouldBe($"\"{correlation}\"");
        TraceId().IsMatch(CapturingLogSink.Property(summary, "TraceId").Trim('"')).ShouldBeTrue();
        SpanId().IsMatch(CapturingLogSink.Property(summary, "SpanId").Trim('"')).ShouldBeTrue();
        CapturingLogSink.Property(summary, "RequestRoute").ShouldBe("\"/health/live\"");
        CapturingLogSink.Property(summary, "StatusCode").ShouldBe("200");
    }

    [Fact]
    public async Task RequestSummary_OfAHealthCheck_IsVerbose()
    {
        var (sink, correlation) = await RequestAsync("/health/live");

        Summary(sink, correlation).Level.ShouldBe(LogEventLevel.Verbose);
    }

    [Fact]
    public async Task EveryEventOfTheRequest_IsAValidCompactJsonLine()
    {
        var (sink, _) = await RequestAsync("/health/live");

        sink.Events.ShouldNotBeEmpty();

        foreach (var logEvent in sink.Events)
        {
            var line = CapturingLogSink.Json(logEvent);
            line.ShouldNotContain("\n");

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            root.TryGetProperty("@t", out _).ShouldBeTrue(line);
            root.TryGetProperty("@m", out _).ShouldBeTrue(line);
            root.TryGetProperty("@i", out _).ShouldBeTrue(line);
            root.TryGetProperty("@l", out _).ShouldBe(logEvent.Level != LogEventLevel.Information, $"{logEvent.Level}: {line}");
        }
    }

    [Fact]
    public async Task EveryEventOfTheRequest_CarriesTheIdentityOfTheService()
    {
        var (sink, _) = await RequestAsync("/health/live");

        sink.Events.ShouldNotBeEmpty();
        sink.Events.ShouldAllBe(candidate => CapturingLogSink.Property(candidate, "Service") == "\"ledger-api\"");
        sink.Events.ShouldAllBe(candidate => CapturingLogSink.Property(candidate, "Environment") == "\"Testing\"");
    }

    [GeneratedRegex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex TraceId();

    [GeneratedRegex("^[0-9a-f]{16}$", RegexOptions.CultureInvariant)]
    private static partial Regex SpanId();
}
