using Dapper;
using Ledger.Application.Abstractions;
using Ledger.Application.Accounts;
using Ledger.Application.Audit;
using Ledger.Application.Security;
using Ledger.Domain.Shared;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Ledger.Infrastructure.Persistence;

internal sealed class PostgresAccountKeyRewrapper(
    IPostgresConnectionFactory connections,
    IOptions<PostgresOptions> postgres,
    ISecurityTelemetry telemetry) : IAccountKeyRewrapper
{
    internal const string SelectRewrapBatchSql = """
                                                 SELECT id, holder_document_encrypted, holder_document_key_version
                                                 FROM accounts
                                                 WHERE holder_document_key_version < @active_version
                                                   AND id > @after_id
                                                 ORDER BY id
                                                 LIMIT @batch_size
                                                 FOR NO KEY UPDATE SKIP LOCKED;
                                                 """;

    internal const string UpdateRewrappedSql = """
                                               UPDATE accounts AS a
                                               SET holder_document_encrypted = v.encrypted,
                                                   holder_document_blind_index = v.blind_index,
                                                   holder_document_key_version = v.key_version
                                               FROM unnest(@ids::uuid[], @encrypted::bytea[], @blind_indexes::bytea[], @key_versions::integer[])
                                                    AS v(id, encrypted, blind_index, key_version)
                                               WHERE a.id = v.id
                                                 AND a.holder_document_key_version < v.key_version;
                                               """;

    internal const string KeyUsageSql = """
                                        SELECT holder_document_key_version, count(*)
                                        FROM accounts
                                        WHERE holder_document_key_version IS NOT NULL
                                        GROUP BY holder_document_key_version;
                                        """;

    private const long PassLockKey = 727003;
    private const string TryPassLockSql = "SELECT pg_try_advisory_lock(@key);";
    private const string ReleasePassLockSql = "SELECT pg_advisory_unlock(@key);";

    private static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(5);

    private const PostgresSource Source = PostgresSource.Worker;

    public async Task<IAsyncDisposable?> TryBeginPassAsync(CancellationToken cancellationToken)
    {
        var connection = await connections.OpenConnectionAsync(Source, cancellationToken);

        try
        {
            await using var command = new NpgsqlCommand(TryPassLockSql, connection);
            command.Parameters.Add("key", NpgsqlDbType.Bigint).Value = PassLockKey;

            if (await command.ExecuteScalarAsync(cancellationToken) is true)
            {
                return new PassLock(connection);
            }
        }
        catch
        {
            NpgsqlConnection.ClearPool(connection);
            await connection.DisposeAsync();

            throw;
        }

        await connection.DisposeAsync();

        return null;
    }

    public async Task<KeyUsage> ReadKeyUsageAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenConnectionAsync(Source, cancellationToken);
        await using var command = new NpgsqlCommand(KeyUsageSql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var accounts = new Dictionary<int, long>();

        while (await reader.ReadAsync(cancellationToken))
        {
            accounts[reader.GetInt32(0)] = reader.GetInt64(1);
        }

        return new KeyUsage(accounts);
    }

    public async Task<RewrapBatchResult> RewrapBatchAsync(
        RewrapBatchRequest request,
        Func<RewrapRow, Result<ProtectedHolderDocument>> transform,
        CancellationToken cancellationToken)
    {
        var commandTimeout = postgres.Value.For(Source).CommandTimeoutSeconds;

        await using var connection = await connections.OpenConnectionAsync(Source, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var selected = await SelectAsync(connection, transaction, request, commandTimeout, cancellationToken);

        if (selected.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);

            return new RewrapBatchResult(0, 0, 0, null);
        }

        var rewrapped = new List<(RewrapRow Row, ProtectedHolderDocument Document)>(selected.Count);
        var failed = 0;

        foreach (var row in selected)
        {
            var result = transform(row);

            if (result.IsSuccess)
            {
                rewrapped.Add((row, result.Value));
            }
            else
            {
                failed++;
            }
        }

        var updated = rewrapped.Count == 0
            ? 0
            : await UpdateAsync(connection, transaction, rewrapped, commandTimeout, cancellationToken);

        await RecordAuditAsync(connection, transaction, request, selected, updated, failed, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new RewrapBatchResult(selected.Count, updated, failed, selected[^1].Id);
    }

    private sealed class PassLock(NpgsqlConnection connection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                using var deadline = new CancellationTokenSource(ReleaseTimeout);
                await using var command = new NpgsqlCommand(ReleasePassLockSql, connection);
                command.Parameters.Add("key", NpgsqlDbType.Bigint).Value = PassLockKey;

                await command.ExecuteNonQueryAsync(deadline.Token);
            }
            catch (Exception exception) when (exception is NpgsqlException or OperationCanceledException or InvalidOperationException)
            {
                NpgsqlConnection.ClearPool(connection);
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }

    private static async Task<List<RewrapRow>> SelectAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        RewrapBatchRequest request,
        int commandTimeout,
        CancellationToken cancellationToken)
    {
        var parameters = new SqlParameters()
            .Integer("active_version", request.ActiveVersion)
            .Uuid("after_id", request.AfterId?.Value ?? Guid.Empty)
            .Integer("batch_size", request.BatchSize)
            .Build();

        var command = new CommandDefinition(
            SelectRewrapBatchSql,
            parameters,
            transaction,
            commandTimeout,
            cancellationToken: cancellationToken);

        var rows = new List<RewrapRow>(request.BatchSize);

        await using var reader = await connection.ExecuteReaderAsync(command);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new RewrapRow(
                RowMapping.RequireAccountId(reader.GetGuid(0)),
                await reader.GetFieldValueAsync<byte[]>(1, cancellationToken),
                reader.GetInt32(2)));
        }

        return rows;
    }

    private static async Task<int> UpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        List<(RewrapRow Row, ProtectedHolderDocument Document)> rewrapped,
        int commandTimeout,
        CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters();
        parameters.Add(
            "ids",
            new TypedParameter(NpgsqlDbType.Array | NpgsqlDbType.Uuid, rewrapped.Select(item => item.Row.Id.Value).ToArray()));
        parameters.Add(
            "encrypted",
            new TypedParameter(NpgsqlDbType.Array | NpgsqlDbType.Bytea, rewrapped.Select(item => item.Document.Encrypted.ToArray()).ToArray()));
        parameters.Add(
            "blind_indexes",
            new TypedParameter(NpgsqlDbType.Array | NpgsqlDbType.Bytea, rewrapped.Select(item => item.Document.BlindIndex.ToArray()).ToArray()));
        parameters.Add(
            "key_versions",
            new TypedParameter(NpgsqlDbType.Array | NpgsqlDbType.Integer, rewrapped.Select(item => item.Document.KeyVersion).ToArray()));

        var command = new CommandDefinition(
            UpdateRewrappedSql,
            parameters,
            transaction,
            commandTimeout,
            cancellationToken: cancellationToken);

        return await connection.ExecuteAsync(command);
    }

    private async Task RecordAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        RewrapBatchRequest request,
        List<RewrapRow> selected,
        int updated,
        int failed,
        CancellationToken cancellationToken)
    {
        var trail = new PostgresScopedAuditTrail(connection, transaction, telemetry);

        if (updated > 0)
        {
            await trail.RecordAsync(AuditEvents.PiiDecrypted(request.PassId, updated), cancellationToken);
        }

        var fromVersion = selected.Min(row => row.KeyVersion);

        await trail.RecordAsync(
            AuditEvents.PiiRewrapped(request.PassId, fromVersion, request.ActiveVersion, updated, failed),
            cancellationToken);
    }
}
