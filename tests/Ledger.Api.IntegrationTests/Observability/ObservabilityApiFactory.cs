using Ledger.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;

namespace Ledger.Api.IntegrationTests.Observability;

internal sealed class ObservabilityApiFactory : LedgerApiFactory
{
    private static readonly Dictionary<string, string?> VerboseLogging = new()
    {
        ["Serilog:MinimumLevel:Default"] = "Verbose",
        ["Serilog:MinimumLevel:Override:Microsoft"] = "Verbose",
        ["Serilog:MinimumLevel:Override:Microsoft.AspNetCore"] = "Verbose",
        ["Serilog:MinimumLevel:Override:Npgsql"] = "Verbose",
        ["Serilog:MinimumLevel:Override:System"] = "Verbose"
    };

    private readonly Action<IServiceCollection>? _configureServices;

    public ObservabilityApiFactory(
        CapturingLogSink sink,
        IReadOnlyDictionary<string, string?>? overrides = null,
        Action<IServiceCollection>? configureServices = null)
        : base(Merge(TestConfiguration.ForUnreachablePostgres(), VerboseLogging, overrides))
    {
        Sink = sink;
        _configureServices = configureServices;
    }

    public CapturingLogSink Sink { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ILogEventSink>(Sink);
            _configureServices?.Invoke(services);
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
