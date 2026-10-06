using System.Diagnostics;
using System.Text.RegularExpressions;
using Ledger.Infrastructure.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Serilog.Events;

namespace Ledger.Api.IntegrationTests.Observability;

[Trait("Category", "Integration")]
[Collection(OtelEnvironment.Collection)]
public sealed partial class SamplingFromEnvironmentTests
{
    private const string RequestSpan = "Microsoft.AspNetCore.Hosting.HttpRequestIn";
    private const string ErrorRoute = "/__telemetry-test/error";

    private static void Capture(IServiceCollection services, CapturingActivityExporter exporter)
    {
        services.ConfigureOpenTelemetryTracerProvider(
            tracing => tracing.AddProcessor(new SimpleActivityExportProcessor(exporter)));
    }

    private static async Task SendAsync(HttpClient client, int requests, string route)
    {
        foreach (var batch in Enumerable.Range(0, requests).Chunk(50))
        {
            await Task.WhenAll(batch.Select(async _ =>
            {
                using var response = await client.GetAsync(route, CancellationToken.None);
            }));
        }
    }

    [Fact]
    public async Task WithoutTheEndpoint_NothingTriesToExportAndTheLogStaysClean()
    {
        using var clean = OtelEnvironment.Clean();
        var sink = new CapturingLogSink();
        using var factory = new ObservabilityApiFactory(sink);
        using var client = factory.CreateClient();

        await SendAsync(client, 20, "/health/live");
        factory.Services.GetRequiredService<TracerProvider>().ForceFlush(2000);

        factory.Services.GetRequiredService<TelemetryExportSettings>().Traces.ShouldBeFalse();
        factory.Services.GetRequiredService<TelemetryExportSettings>().Metrics.ShouldBeFalse();
        sink.Events
            .Where(logEvent => logEvent.Level >= LogEventLevel.Warning)
            .Where(logEvent => CapturingLogSink.Property(logEvent, "SourceContext").Contains("OpenTelemetry", StringComparison.Ordinal))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task SamplerAlwaysOff_SamplesNoRequestSpan()
    {
        using var clean = OtelEnvironment.Clean();
        using var exporter = new CapturingActivityExporter();
        using var factory = new ObservabilityApiFactory(
            new CapturingLogSink(),
            new Dictionary<string, string?> { ["OTEL_TRACES_SAMPLER"] = "always_off" },
            services => Capture(services, exporter));
        using var client = factory.CreateClient();

        await SendAsync(client, 200, "/health/live");
        factory.Services.GetRequiredService<TracerProvider>().ForceFlush(5000).ShouldBeTrue();

        exporter.Names.Count(name => name == RequestSpan).ShouldBe(0);
    }

    [Fact]
    public async Task SamplerParentBasedTraceIdRatio_SamplesAboutTenPercentOfFiveThousandRequests()
    {
        const int requests = 5000;
        using var clean = OtelEnvironment.Clean();
        using var exporter = new CapturingActivityExporter();
        using var factory = new ObservabilityApiFactory(
            new CapturingLogSink(),
            new Dictionary<string, string?>
            {
                ["OTEL_TRACES_SAMPLER"] = "parentbased_traceidratio",
                ["OTEL_TRACES_SAMPLER_ARG"] = "0.1"
            },
            services => Capture(services, exporter));
        using var client = factory.CreateClient();

        await SendAsync(client, requests, "/health/live");
        factory.Services.GetRequiredService<TracerProvider>().ForceFlush(10000).ShouldBeTrue();

        var fraction = exporter.Names.Count(name => name == RequestSpan) / (double)requests;
        fraction.ShouldBeInRange(0.07, 0.13);
    }

    [Fact]
    public async Task SamplerNotConfigured_SamplesEveryRequest()
    {
        using var clean = OtelEnvironment.Clean();
        using var exporter = new CapturingActivityExporter();
        using var factory = new ObservabilityApiFactory(
            new CapturingLogSink(),
            configureServices: services => Capture(services, exporter));
        using var client = factory.CreateClient();

        await SendAsync(client, 100, "/health/live");
        factory.Services.GetRequiredService<TracerProvider>().ForceFlush(5000).ShouldBeTrue();

        exporter.Names.Count(name => name == RequestSpan).ShouldBe(100);
    }

    [Fact]
    public void TheCode_NeverDefinesASampler()
    {
        var sources = Directory.EnumerateFiles(RepositoryPaths.Source, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        var offenders = sources
            .Where(path => SamplerDefinition().IsMatch(File.ReadAllText(path)))
            .Select(Path.GetFileName)
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public async Task ErrorLog_OfARequestWhoseSpanWasDropped_StillCarriesTheTraceId()
    {
        using var clean = OtelEnvironment.Clean();
        var sink = new CapturingLogSink();
        using var factory = new ObservabilityApiFactory(
            sink,
            new Dictionary<string, string?> { ["OTEL_TRACES_SAMPLER"] = "always_off" },
            services => services.AddTransient<IStartupFilter, ErrorRouteStartupFilter>());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(ErrorRoute, CancellationToken.None);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.InternalServerError);
        var error = sink.Events.Single(logEvent => logEvent.Level == LogEventLevel.Error
                                                   && logEvent.MessageTemplate.Text == ErrorRouteStartupFilter.Message);
        TraceId().IsMatch(CapturingLogSink.Property(error, "TraceId").Trim('"')).ShouldBeTrue();
    }

    [Fact]
    public async Task DeadCollector_NeverDelaysAResponse()
    {
        using var environment = OtelEnvironment.Exporting("http://127.0.0.1:1");
        using var factory = new ObservabilityApiFactory(new CapturingLogSink());
        using var client = factory.CreateClient();
        using var warmup = await client.GetAsync("/health/live", CancellationToken.None);
        var slowest = TimeSpan.Zero;
        var total = Stopwatch.StartNew();

        for (var index = 0; index < 200; index++)
        {
            var watch = Stopwatch.StartNew();
            using var response = await client.GetAsync("/health/live", CancellationToken.None);
            watch.Stop();
            slowest = watch.Elapsed > slowest ? watch.Elapsed : slowest;
        }

        total.Stop();

        factory.Services.GetRequiredService<TelemetryExportSettings>().Traces.ShouldBeTrue();
        slowest.ShouldBeLessThan(TimeSpan.FromSeconds(2));
        total.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(20));

        factory.Services.GetRequiredService<TracerProvider>().Dispose();
    }

    [GeneratedRegex(@"\bSetSampler\s*\(|\bAddSampler\s*\(|\bnew\s+(AlwaysOn|AlwaysOff|TraceIdRatioBased|ParentBased)Sampler\b", RegexOptions.CultureInvariant)]
    private static partial Regex SamplerDefinition();

    [GeneratedRegex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex TraceId();

    private sealed class ErrorRouteStartupFilter : IStartupFilter
    {
        public const string Message = "Synthetic error raised by the telemetry test";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            ArgumentNullException.ThrowIfNull(next);

            return app =>
            {
                app.Use(async (context, following) =>
                {
                    if (context.Request.Path != ErrorRoute)
                    {
                        await following(context);

                        return;
                    }

                    context.RequestServices.GetRequiredService<Serilog.ILogger>().Error(Message);
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                });

                next(app);
            };
        }
    }
}
