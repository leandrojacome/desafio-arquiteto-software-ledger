using Ledger.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Api.IntegrationTests.Reads;

internal sealed class ReadApiFactory : LedgerApiFactory
{
    private readonly Action<IServiceCollection>? _configureServices;

    private ReadApiFactory(IReadOnlyDictionary<string, string?> configuration, Action<IServiceCollection>? configureServices)
        : base(configuration)
    {
        _configureServices = configureServices;
    }

    public static ReadApiFactory WithDatabase(
        PostgresFixture postgres,
        IReadOnlyDictionary<string, string?>? overrides = null,
        Action<IServiceCollection>? configureServices = null)
    {
        ArgumentNullException.ThrowIfNull(postgres);

        return new ReadApiFactory(Merge(postgres.Configuration, overrides), configureServices);
    }

    public static ReadApiFactory WithoutDatabase(
        IReadOnlyDictionary<string, string?>? overrides = null,
        Action<IServiceCollection>? configureServices = null)
    {
        return new ReadApiFactory(Merge(TestConfiguration.ForUnreachablePostgres(), overrides), configureServices);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services => _configureServices?.Invoke(services));
    }

    private static Dictionary<string, string?> Merge(
        IReadOnlyDictionary<string, string?> baseline,
        IReadOnlyDictionary<string, string?>? overrides)
    {
        var merged = new Dictionary<string, string?>(baseline, StringComparer.Ordinal);

        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
        {
            merged[key] = value;
        }

        return merged;
    }
}
