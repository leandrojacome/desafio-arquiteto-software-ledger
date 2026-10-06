using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Persistence.Support;
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

internal sealed class ReadWorld : IAsyncDisposable
{
    private ReadWorld(
        ReadApiFactory factory,
        LedgerHost host,
        ReadApiClient client,
        PostgresFixture postgres)
    {
        Factory = factory;
        Host = host;
        Client = client;
        Postgres = postgres;
        Ledger = new ReadLedger(postgres);
        Queries = new LedgerQueries(postgres);
        Seeder = new LedgerSeeder(postgres);
    }

    public ReadApiFactory Factory { get; }

    public LedgerHost Host { get; }

    public ReadApiClient Client { get; }

    public PostgresFixture Postgres { get; }

    public ReadLedger Ledger { get; }

    public LedgerQueries Queries { get; }

    public LedgerSeeder Seeder { get; }

    public static ReadWorld Create(
        PostgresFixture postgres,
        IReadOnlyDictionary<string, string?>? overrides = null,
        Action<IServiceCollection>? configureServices = null,
        string? token = null,
        IReadOnlyDictionary<string, string?>? hostOverrides = null)
    {
        var factory = ReadApiFactory.WithDatabase(postgres, overrides, configureServices);
        var host = LedgerHost.Create(postgres, hostOverrides);

        return new ReadWorld(factory, host, ReadApiClient.For(factory, token), postgres);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await Host.DisposeAsync();
        await Factory.DisposeAsync();
    }
}
