using Dapper;
using Ledger.Application.Abstractions;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;
using Npgsql;

namespace Ledger.Infrastructure.Persistence;

internal sealed class PostgresIdempotencyStore(NpgsqlConnection connection, NpgsqlTransaction transaction)
    : IIdempotencyStore
{
    private const int ReplayEntryOffset = 2;
    private const int AccountMissing = 0;
    private const int KeyReserved = 1;

    private const string ReserveKeySql = """
                                         WITH account AS (
                                             SELECT id FROM accounts WHERE id = @account_id
                                         ),
                                         reserved AS (
                                             INSERT INTO idempotency_keys (account_id, idempotency_key, request_hash, hash_version, entry_id)
                                             SELECT id, @idempotency_key, @request_hash, @hash_version, @entry_id
                                             FROM account
                                             ON CONFLICT (account_id, idempotency_key) DO NOTHING
                                             RETURNING 1
                                         )
                                         SELECT CASE
                                                    WHEN NOT EXISTS (SELECT 1 FROM account) THEN 0
                                                    WHEN EXISTS (SELECT 1 FROM reserved) THEN 1
                                                    ELSE 2
                                                END;
                                         """;

    private const string ReadReplaySql = """
                                         SELECT k.request_hash, k.hash_version,
                                                e.id, e.account_id, e.account_version, e.type, e.amount, e.currency, e.balance_after,
                                                e.recorded_at, e.occurred_at, e.description, e.reference, e.reverses_entry_id
                                         FROM idempotency_keys AS k
                                         JOIN ledger_entries AS e ON e.id = k.entry_id
                                         WHERE k.account_id = @account_id
                                           AND k.idempotency_key = @idempotency_key;
                                         """;

    public async Task<Result<bool>> TryReserveAsync(
        AccountId accountId,
        IdempotencyKey key,
        ReadOnlyMemory<byte> requestHash,
        int hashVersion,
        EntryId entryId,
        CancellationToken cancellationToken)
    {
        var parameters = new SqlParameters()
            .Uuid("account_id", accountId.Value)
            .Varchar("idempotency_key", key.Value)
            .Bytea("request_hash", requestHash.ToArray())
            .Smallint("hash_version", checked((short)hashVersion))
            .Uuid("entry_id", entryId.Value)
            .Build();

        var command = new CommandDefinition(ReserveKeySql, parameters, transaction, cancellationToken: cancellationToken);

        var outcome = await connection.ExecuteScalarAsync<int>(command);

        return outcome switch
        {
            AccountMissing => AccountErrors.NotFound,
            KeyReserved => true,
            _ => false
        };
    }

    public async Task<IdempotencyRecord?> FindAsync(
        AccountId accountId,
        IdempotencyKey key,
        CancellationToken cancellationToken)
    {
        var parameters = new SqlParameters()
            .Uuid("account_id", accountId.Value)
            .Varchar("idempotency_key", key.Value)
            .Build();

        var command = new CommandDefinition(ReadReplaySql, parameters, transaction, cancellationToken: cancellationToken);

        await using var reader = await connection.ExecuteReaderAsync(command);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var requestHash = await reader.GetFieldValueAsync<byte[]>(0, cancellationToken);
        var entry = await RowMapping.ReadEntryAsync(reader, ReplayEntryOffset, cancellationToken);

        return new IdempotencyRecord(requestHash, reader.GetInt16(1), entry);
    }
}
