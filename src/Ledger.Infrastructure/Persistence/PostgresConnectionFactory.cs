using System.Collections.Frozen;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Ledger.Infrastructure.Persistence;

internal sealed class PostgresConnectionFactory : IPostgresConnectionFactory, IAsyncDisposable
{
    private readonly FrozenDictionary<PostgresSource, NpgsqlDataSource> _dataSources;

    public PostgresConnectionFactory(
        IOptions<PostgresOptions> options,
        PostgresSourceSelection selection,
        ILoggerFactory loggerFactory)
    {
        var settings = options.Value;

        _dataSources = selection.Sources.ToFrozenDictionary(
            source => source,
            source => CreateDataSource(settings, source, loggerFactory));
    }

    public ValueTask<NpgsqlConnection> OpenConnectionAsync(PostgresSource source, CancellationToken cancellationToken)
    {
        if (!_dataSources.TryGetValue(source, out var dataSource))
        {
            throw new InvalidOperationException($"The PostgreSQL source {source} is not configured for this process.");
        }

        return dataSource.OpenConnectionAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var dataSource in _dataSources.Values)
        {
            await dataSource.DisposeAsync();
        }
    }

    private static NpgsqlDataSource CreateDataSource(
        PostgresOptions options,
        PostgresSource source,
        ILoggerFactory loggerFactory)
    {
        var connectionString = PostgresConnectionString.Build(options, source);

        return new NpgsqlDataSourceBuilder(connectionString)
            .UseLoggerFactory(loggerFactory)
            .Build();
    }
}
