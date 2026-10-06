using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Ledger.Infrastructure.Persistence;

internal sealed class PostgresPoolWarmupService(
    IPostgresConnectionFactory connectionFactory,
    PostgresSourceSelection selection,
    IOptions<PostgresOptions> options,
    ILogger<PostgresPoolWarmupService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var source in selection.Sources.Where(candidate => candidate != PostgresSource.Migrator))
        {
            var minimum = options.Value.For(source).MinPoolSize;

            if (minimum > 0)
            {
                await WarmAsync(source, minimum, cancellationToken);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task WarmAsync(PostgresSource source, int minimum, CancellationToken cancellationToken)
    {
        var held = new List<NpgsqlConnection>(minimum);

        try
        {
            for (var index = 0; index < minimum; index++)
            {
                held.Add(await connectionFactory.OpenConnectionAsync(source, cancellationToken));
            }

            PersistenceLog.ConnectionPoolWarmedUp(logger, source, held.Count);
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException)
        {
            PersistenceLog.ConnectionPoolWarmupFailed(logger, source, exception);
        }
        finally
        {
            foreach (var connection in held)
            {
                await connection.DisposeAsync();
            }
        }
    }
}
