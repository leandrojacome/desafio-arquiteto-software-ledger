using Ledger.Api.IntegrationTests.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;

namespace Ledger.Api.IntegrationTests.Infrastructure;

public class TestApiFactory : LedgerApiFactory
{
    private readonly string _environment;
    private readonly Action<IEndpointRouteBuilder>? _routes;
    private readonly Action<IServiceCollection>? _services;

    protected TestApiFactory(
        IReadOnlyDictionary<string, string?>? overrides = null,
        string environment = "Testing",
        Action<IEndpointRouteBuilder>? routes = null,
        Action<IServiceCollection>? services = null)
        : base(Merge(TestConfiguration.ForUnreachablePostgres(), Logging, overrides))
    {
        _environment = environment;
        _routes = routes;
        _services = services;
        Sink = new CapturingLogSink();
    }

    internal CapturingLogSink Sink { get; }

    internal static TestApiFactory With(
        IReadOnlyDictionary<string, string?>? overrides = null,
        string environment = "Testing",
        Action<IEndpointRouteBuilder>? routes = null,
        Action<IServiceCollection>? services = null) => new(overrides, environment, routes, services);

    private static Dictionary<string, string?> Logging => new()
    {
        ["Serilog:MinimumLevel:Default"] = "Information",
        ["Serilog:MinimumLevel:Override:Microsoft"] = "Warning",
        ["Serilog:MinimumLevel:Override:Microsoft.AspNetCore"] = "Warning",
        ["Serilog:MinimumLevel:Override:Npgsql"] = "Warning",
        ["Serilog:MinimumLevel:Override:System"] = "Warning"
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        base.ConfigureWebHost(builder);

        builder.UseEnvironment(_environment);
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ILogEventSink>(Sink);
            services.AddTransient<IStartupFilter>(_ => new TestEndpointsStartupFilter(_routes));
            _services?.Invoke(services);
        });
    }

    private static Dictionary<string, string?> Merge(params IReadOnlyDictionary<string, string?>?[] sources)
    {
        var merged = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var source in sources.OfType<IReadOnlyDictionary<string, string?>>())
        {
            foreach (var (key, value) in source)
            {
                merged[key] = value;
            }
        }

        return merged;
    }
}
