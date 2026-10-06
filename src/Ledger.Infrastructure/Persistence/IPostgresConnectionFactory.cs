using Npgsql;

namespace Ledger.Infrastructure.Persistence;

internal interface IPostgresConnectionFactory
{
    ValueTask<NpgsqlConnection> OpenConnectionAsync(PostgresSource source, CancellationToken cancellationToken);
}
