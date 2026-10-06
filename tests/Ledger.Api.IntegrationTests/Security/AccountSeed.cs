using Ledger.Application.Abstractions;
using Ledger.Application.Security;
using Ledger.Domain.Accounts;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Security;

internal static class AccountSeed
{
    private const string InsertAccountSql = """
                                            INSERT INTO accounts (id, currency, holder_document_encrypted, holder_document_blind_index, holder_document_key_version)
                                            VALUES (@id, 'BRL', @encrypted, @blind_index, @key_version)
                                            """;

    private const string InsertBalanceSql = """
                                            INSERT INTO account_balances (account_id, balance, overdraft_limit, version, last_recorded_at)
                                            VALUES (@id, 0, 0, 0, clock_timestamp())
                                            """;

    private const string ReadAccountSql = """
                                          SELECT holder_document_encrypted, holder_document_blind_index, holder_document_key_version
                                          FROM accounts
                                          WHERE id = @id
                                          """;

    public static async Task<IReadOnlyList<SeededAccount>> InsertManyAsync(
        NpgsqlConnection connection,
        IHolderDocumentProtector protector,
        int count,
        int firstDocumentIndex = 0)
    {
        var accounts = new List<SeededAccount>(count);

        for (var index = 0; index < count; index++)
        {
            var document = CpfFactory.Document(firstDocumentIndex + index);
            var id = AccountId.From(Guid.NewGuid()).Value;

            await InsertAsync(connection, id, protector.Protect(document, id));

            accounts.Add(new SeededAccount(id, document));
        }

        return accounts;
    }

    public static async Task InsertAsync(NpgsqlConnection connection, AccountId id, ProtectedHolderDocument? document)
    {
        await using var command = new NpgsqlCommand(InsertAccountSql, connection);
        command.Parameters.AddWithValue("id", id.Value);

        if (document is null)
        {
            command.Parameters.AddWithValue("encrypted", DBNull.Value);
            command.Parameters.AddWithValue("blind_index", DBNull.Value);
            command.Parameters.AddWithValue("key_version", DBNull.Value);
        }
        else
        {
            command.Parameters.AddWithValue("encrypted", document.Encrypted.ToArray());
            command.Parameters.AddWithValue("blind_index", document.BlindIndex.ToArray());
            command.Parameters.AddWithValue("key_version", document.KeyVersion);
        }

        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    public static async Task InsertBalanceAsync(NpgsqlConnection connection, AccountId id)
    {
        await using var command = new NpgsqlCommand(InsertBalanceSql, connection);
        command.Parameters.AddWithValue("id", id.Value);

        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    public static async Task<StoredDocument> ReadAsync(SecurityDatabase database, AccountId id) =>
        await ReadAsync(await database.WorkerReaderAsync(), id);

    public static async Task<StoredDocument> ReadAsync(NpgsqlConnection connection, AccountId id)
    {
        await using var command = new NpgsqlCommand(ReadAccountSql, connection);
        command.Parameters.AddWithValue("id", id.Value);

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        await reader.ReadAsync(CancellationToken.None);

        return new StoredDocument(
            await reader.IsDBNullAsync(0) ? null : await reader.GetFieldValueAsync<byte[]>(0),
            await reader.IsDBNullAsync(1) ? null : await reader.GetFieldValueAsync<byte[]>(1),
            await reader.IsDBNullAsync(2) ? null : reader.GetInt32(2));
    }
}
