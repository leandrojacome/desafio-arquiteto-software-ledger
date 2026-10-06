using Ledger.Application.Integrity;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace Ledger.Infrastructure.Persistence.Integrity;

internal sealed class PostgresIntegritySessions(
    IPostgresConnectionFactory connectionFactory,
    TimeProvider timeProvider,
    ILogger<PostgresIntegritySession> logger) : IIntegritySessions
{
    private const int LockNamespace = 727002;

    private const string TryLockSql = "SELECT pg_try_advisory_lock(@namespace, @mode_key);";

    public async Task<IIntegritySession?> TryBeginRunAsync(IntegrityMode mode, CancellationToken cancellationToken)
    {
        var connection = await connectionFactory.OpenConnectionAsync(PostgresSource.Worker, cancellationToken);

        bool acquired;

        try
        {
            acquired = await TryLockAsync(connection, mode, cancellationToken);
        }
        catch
        {
            NpgsqlConnection.ClearPool(connection);
            await connection.DisposeAsync();

            throw;
        }

        if (!acquired)
        {
            await connection.DisposeAsync();

            return null;
        }

        return new PostgresIntegritySession(connection, mode.LockKey(), LockNamespace, timeProvider, logger);
    }

    public async Task<IIntegritySession> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = await connectionFactory.OpenConnectionAsync(PostgresSource.Worker, cancellationToken);

        return new PostgresIntegritySession(connection, null, LockNamespace, timeProvider, logger);
    }

    private static async Task<bool> TryLockAsync(
        NpgsqlConnection connection,
        IntegrityMode mode,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(TryLockSql, connection);

        command.Parameters.Add("namespace", NpgsqlDbType.Integer).Value = LockNamespace;
        command.Parameters.Add("mode_key", NpgsqlDbType.Integer).Value = mode.LockKey();

        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }
}
