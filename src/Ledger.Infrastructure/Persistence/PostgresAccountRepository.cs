using Dapper;
using Ledger.Application.Abstractions;
using Ledger.Application.Accounts;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Npgsql;

namespace Ledger.Infrastructure.Persistence;

internal sealed class PostgresAccountRepository(NpgsqlConnection connection, NpgsqlTransaction transaction)
    : IAccountRepository
{
    private const string DiagnoseRefusalSql = """
                                              SELECT a.currency, ab.balance, ab.overdraft_limit
                                              FROM accounts AS a
                                              JOIN account_balances AS ab ON ab.account_id = a.id
                                              WHERE a.id = @account_id;
                                              """;

    private const string CreateAccountSql = """
                                            WITH new_account AS (
                                                INSERT INTO accounts (id, currency, holder_document_encrypted, holder_document_blind_index, holder_document_key_version)
                                                VALUES (@id, @currency, @holder_document_encrypted, @holder_document_blind_index, @holder_document_key_version)
                                                ON CONFLICT (id) DO NOTHING
                                                RETURNING id, created_at
                                            )
                                            INSERT INTO account_balances (account_id, balance, overdraft_limit, version, last_recorded_at)
                                            SELECT id, 0, @overdraft_limit, 0, created_at FROM new_account
                                            RETURNING last_recorded_at AS created_at;
                                            """;

    private const string ReadAccountCreatedAtSql = """
                                                   SELECT created_at
                                                   FROM accounts
                                                   WHERE id = @account_id;
                                                   """;

    private const string ReserveCreationKeySql = """
                                                 INSERT INTO account_creation_keys (client_id, idempotency_key, account_id, request_hash, hash_version)
                                                 VALUES (@client_id, @idempotency_key, @account_id, @request_hash, @hash_version)
                                                 ON CONFLICT (client_id, idempotency_key) DO NOTHING;
                                                 """;

    private const string ReadCreationKeySql = """
                                              SELECT k.request_hash, k.hash_version, k.account_id, a.created_at
                                              FROM account_creation_keys AS k
                                              JOIN accounts AS a ON a.id = k.account_id
                                              WHERE k.client_id = @client_id
                                                AND k.idempotency_key = @idempotency_key;
                                              """;

    public async Task<AccountBalance?> GetForDiagnosisAsync(AccountId accountId, CancellationToken cancellationToken)
    {
        var parameters = new SqlParameters().Uuid("account_id", accountId.Value).Build();
        var command = new CommandDefinition(
            DiagnoseRefusalSql,
            parameters,
            transaction,
            cancellationToken: cancellationToken);

        await using var reader = await connection.ExecuteReaderAsync(command);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var balance = AccountBalance.Create(
            accountId,
            reader.GetString(0),
            reader.GetDecimal(1),
            reader.GetDecimal(2));

        return balance.IsSuccess
            ? balance.Value
            : throw new InvalidOperationException("The stored account balance violates the account rules.");
    }

    public async Task<DateTimeOffset?> CreateAsync(NewAccount account, CancellationToken cancellationToken)
    {
        var parameters = new SqlParameters()
            .Uuid("id", account.Id.Value)
            .Char("currency", account.Currency)
            .Bytea("holder_document_encrypted", account.Document.Encrypted.ToArray())
            .Bytea("holder_document_blind_index", account.Document.BlindIndex.ToArray())
            .Integer("holder_document_key_version", account.Document.KeyVersion)
            .Numeric("overdraft_limit", account.OverdraftLimit.Amount)
            .Build();

        var command = new CommandDefinition(
            CreateAccountSql,
            parameters,
            transaction,
            cancellationToken: cancellationToken);

        await using var reader = await connection.ExecuteReaderAsync(command);

        return await reader.ReadAsync(cancellationToken)
            ? await reader.GetFieldValueAsync<DateTimeOffset>(0, cancellationToken)
            : null;
    }

    public async Task<DateTimeOffset?> GetCreatedAtAsync(AccountId accountId, CancellationToken cancellationToken)
    {
        var parameters = new SqlParameters().Uuid("account_id", accountId.Value).Build();
        var command = new CommandDefinition(
            ReadAccountCreatedAtSql,
            parameters,
            transaction,
            cancellationToken: cancellationToken);

        await using var reader = await connection.ExecuteReaderAsync(command);

        return await reader.ReadAsync(cancellationToken)
            ? await reader.GetFieldValueAsync<DateTimeOffset>(0, cancellationToken)
            : null;
    }

    public async Task<bool> TryReserveCreationKeyAsync(
        AccountCreationKeyReservation reservation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        var parameters = new SqlParameters()
            .Varchar("client_id", reservation.ClientId)
            .Varchar("idempotency_key", reservation.Key.Value)
            .Uuid("account_id", reservation.AccountId.Value)
            .Bytea("request_hash", reservation.RequestHash.ToArray())
            .Smallint("hash_version", checked((short)reservation.HashVersion))
            .Build();

        var command = new CommandDefinition(
            ReserveCreationKeySql,
            parameters,
            transaction,
            cancellationToken: cancellationToken);

        return await connection.ExecuteAsync(command) == 1;
    }

    public async Task<AccountCreationKeyRecord?> FindCreationKeyAsync(
        string clientId,
        IdempotencyKey key,
        CancellationToken cancellationToken)
    {
        var parameters = new SqlParameters()
            .Varchar("client_id", clientId)
            .Varchar("idempotency_key", key.Value)
            .Build();

        var command = new CommandDefinition(
            ReadCreationKeySql,
            parameters,
            transaction,
            cancellationToken: cancellationToken);

        await using var reader = await connection.ExecuteReaderAsync(command);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var requestHash = await reader.GetFieldValueAsync<byte[]>(0, cancellationToken);
        var accountId = AccountId.From(await reader.GetFieldValueAsync<Guid>(2, cancellationToken));

        return accountId.IsSuccess
            ? new AccountCreationKeyRecord(
                requestHash,
                reader.GetInt16(1),
                accountId.Value,
                await reader.GetFieldValueAsync<DateTimeOffset>(3, cancellationToken))
            : throw new InvalidOperationException("The stored account creation key points to an empty account id.");
    }
}
