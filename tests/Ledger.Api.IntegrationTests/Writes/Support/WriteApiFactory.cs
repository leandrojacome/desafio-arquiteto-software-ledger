using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Observability;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog.Core;

namespace Ledger.Api.IntegrationTests.Writes.Support;

internal sealed class WriteApiFactory(
    PostgresFixture postgres,
    IReadOnlyDictionary<string, string?>? overrides = null,
    TimeProvider? time = null,
    Action<IServiceCollection>? configureServices = null,
    CapturingLogSink? logSink = null) : WebApplicationFactory<Program>
{
    public static IReadOnlyDictionary<string, string?> WidePool { get; } = new Dictionary<string, string?>
    {
        ["Postgres:Sources:Write:MaxPoolSize"] = "64",
        ["Postgres:Sources:Write:ConnectionTimeoutSeconds"] = "10",
        ["Postgres:Sources:Write:CommandTimeoutSeconds"] = "15",
        ["Postgres:Sources:Write:LockTimeoutMs"] = "10000",
        ["Postgres:Sources:Write:StatementTimeoutMs"] = "15000",
        ["RateLimiting:WriteConcurrency"] = "64"
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var settings = postgres.ConfigurationWith(overrides ?? new Dictionary<string, string?>());

        if (logSink is not null)
        {
            settings = new Dictionary<string, string?>(settings)
            {
                ["Serilog:MinimumLevel:Default"] = "Verbose",
                ["Serilog:MinimumLevel:Override:Microsoft"] = "Verbose",
                ["Serilog:MinimumLevel:Override:Microsoft.AspNetCore"] = "Verbose",
                ["Serilog:MinimumLevel:Override:Npgsql"] = "Verbose",
                ["Serilog:MinimumLevel:Override:System"] = "Verbose"
            };
        }

        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
        builder.ConfigureTestServices(services =>
        {
            if (time is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(time);
            }

            if (logSink is not null)
            {
                services.AddSingleton<ILogEventSink>(logSink);
            }

            configureServices?.Invoke(services);
        });
    }
}
