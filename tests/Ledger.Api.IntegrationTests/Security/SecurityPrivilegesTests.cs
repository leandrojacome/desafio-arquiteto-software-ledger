using System.Diagnostics.CodeAnalysis;
using Ledger.Api.IntegrationTests.Infrastructure;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Security;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class SecurityPrivilegesTests(PostgresFixture postgres)
{
    private const string Blob = "'\\x010001'::bytea";
    private const string BlindIndex = "'\\x0000000000000000000000000000000000000000000000000000000000000000'::bytea";

    private static readonly string[] DocumentColumns =
        ["holder_document_encrypted", "holder_document_blind_index", "holder_document_key_version"];

    public static TheoryData<string, string, string, bool> TablePrivileges() => new()
    {
        { PostgresFixture.ApiRole, "accounts", "INSERT", true },
        { PostgresFixture.ApiRole, "accounts", "SELECT", false },
        { PostgresFixture.ApiRole, "accounts", "UPDATE", false },
        { PostgresFixture.ApiRole, "accounts", "DELETE", false },
        { PostgresFixture.ApiRole, "account_balances", "INSERT", true },
        { PostgresFixture.ApiRole, "account_balances", "UPDATE", false },
        { PostgresFixture.ApiRole, "audit_log", "INSERT", true },
        { PostgresFixture.ApiRole, "audit_log", "SELECT", false },
        { PostgresFixture.ApiRole, "outbox_messages", "INSERT", true },
        { PostgresFixture.ApiRole, "outbox_messages", "UPDATE", false },
        { PostgresFixture.WorkerRole, "accounts", "SELECT", true },
        { PostgresFixture.WorkerRole, "accounts", "UPDATE", false },
        { PostgresFixture.WorkerRole, "accounts", "INSERT", false },
        { PostgresFixture.WorkerRole, "accounts", "DELETE", false },
        { PostgresFixture.WorkerRole, "audit_log", "INSERT", true },
        { PostgresFixture.WorkerRole, "outbox_messages", "SELECT", true },
        { PostgresFixture.WorkerRole, "outbox_messages", "UPDATE", false },
        { PostgresFixture.WorkerRole, "outbox_messages", "DELETE", true },
        { PostgresFixture.WorkerRole, "ledger_entries", "INSERT", false },
        { PostgresFixture.ReadOnlyRole, "audit_log", "SELECT", true },
        { PostgresFixture.ReadOnlyRole, "audit_log", "INSERT", false },
        { PostgresFixture.ReadOnlyRole, "accounts", "INSERT", false },
        { PostgresFixture.ReadOnlyRole, "accounts", "UPDATE", false }
    };

    public static TheoryData<string, string> NoOneMutatesTheImmutableTables()
    {
        var data = new TheoryData<string, string>();

        foreach (var role in new[] { PostgresFixture.ApiRole, PostgresFixture.WorkerRole, PostgresFixture.ReadOnlyRole })
        {
            foreach (var table in new[] { "audit_log", "ledger_entries" })
            {
                foreach (var privilege in new[] { "UPDATE", "DELETE", "TRUNCATE" })
                {
                    data.Add(role, $"{table}:{privilege}");
                }
            }
        }

        return data;
    }

    public static TheoryData<string, string, bool> DocumentColumnPrivileges() => new()
    {
        { PostgresFixture.ApiRole, "SELECT", false },
        { PostgresFixture.ApiRole, "INSERT", true },
        { PostgresFixture.ApiRole, "UPDATE", false },
        { PostgresFixture.WorkerRole, "SELECT", true },
        { PostgresFixture.WorkerRole, "UPDATE", true },
        { PostgresFixture.WorkerRole, "INSERT", false },
        { PostgresFixture.ReadOnlyRole, "SELECT", false },
        { PostgresFixture.ReadOnlyRole, "UPDATE", false },
        { PostgresFixture.ReadOnlyRole, "INSERT", false }
    };

    [DockerTheory]
    [MemberData(nameof(TablePrivileges))]
    public async Task Role_HasExactlyTheTablePrivilegeTheDesignGrants(string role, string table, string privilege, bool expected)
    {
        await using var connection = await postgres.AdministrativeSource.OpenConnectionAsync(CancellationToken.None);

        (await HasTablePrivilegeAsync(connection, role, table, privilege)).ShouldBe(expected);
    }

    [DockerTheory]
    [MemberData(nameof(NoOneMutatesTheImmutableTables))]
    public async Task Role_NeverHoldsUpdateDeleteOrTruncateOnTheImmutableTables(string role, string tablePrivilege)
    {
        var parts = tablePrivilege.Split(':');
        await using var connection = await postgres.AdministrativeSource.OpenConnectionAsync(CancellationToken.None);

        (await HasTablePrivilegeAsync(connection, role, parts[0], parts[1])).ShouldBeFalse();
    }

    [DockerTheory]
    [MemberData(nameof(DocumentColumnPrivileges))]
    public async Task Role_HasExactlyThePrivilegeTheDesignGrantsOnEachDocumentColumn(string role, string privilege, bool expected)
    {
        await using var connection = await postgres.AdministrativeSource.OpenConnectionAsync(CancellationToken.None);

        foreach (var column in DocumentColumns)
        {
            (await HasColumnPrivilegeAsync(connection, role, column, privilege))
                .ShouldBe(expected, $"{role} {privilege} on {column}");
        }
    }

    public static TheoryData<string, string, string, string, bool> ColumnPrivileges() => new()
    {
        { PostgresFixture.ApiRole, "account_balances", "balance", "UPDATE", true },
        { PostgresFixture.ApiRole, "account_balances", "version", "UPDATE", true },
        { PostgresFixture.ApiRole, "account_balances", "last_entry_id", "UPDATE", true },
        { PostgresFixture.ApiRole, "account_balances", "last_recorded_at", "UPDATE", true },
        { PostgresFixture.ApiRole, "account_balances", "overdraft_limit", "UPDATE", false },
        { PostgresFixture.ApiRole, "account_balances", "account_id", "UPDATE", false },
        { PostgresFixture.ApiRole, "accounts", "id", "SELECT", true },
        { PostgresFixture.ApiRole, "accounts", "currency", "SELECT", true },
        { PostgresFixture.ApiRole, "accounts", "created_at", "SELECT", true },
        { PostgresFixture.WorkerRole, "accounts", "holder_document_encrypted", "UPDATE", true },
        { PostgresFixture.WorkerRole, "accounts", "holder_document_blind_index", "UPDATE", true },
        { PostgresFixture.WorkerRole, "accounts", "holder_document_key_version", "UPDATE", true },
        { PostgresFixture.WorkerRole, "accounts", "currency", "UPDATE", false },
        { PostgresFixture.WorkerRole, "accounts", "id", "UPDATE", false },
        { PostgresFixture.WorkerRole, "accounts", "created_at", "UPDATE", false },
        { PostgresFixture.WorkerRole, "outbox_messages", "published_at", "UPDATE", true },
        { PostgresFixture.WorkerRole, "outbox_messages", "locked_until", "UPDATE", true },
        { PostgresFixture.WorkerRole, "outbox_messages", "attempts", "UPDATE", true },
        { PostgresFixture.WorkerRole, "outbox_messages", "payload", "UPDATE", false },
        { PostgresFixture.WorkerRole, "outbox_messages", "type", "UPDATE", false },
        { PostgresFixture.WorkerRole, "outbox_messages", "account_id", "UPDATE", false },
        { PostgresFixture.WorkerRole, "outbox_messages", "created_at", "UPDATE", false },
        { PostgresFixture.WorkerRole, "outbox_messages", "id", "UPDATE", false }
    };

    public static TheoryData<string, string, string> TamperingStatements() => new()
    {
        { PostgresFixture.ApiRole, "overdraft limit raised", "UPDATE account_balances SET overdraft_limit = 1000000 WHERE false" },
        { PostgresFixture.ApiRole, "balance row repointed", "UPDATE account_balances SET account_id = account_id WHERE false" },
        { PostgresFixture.ApiRole, "document blob read", "SELECT holder_document_encrypted FROM accounts WHERE false" },
        { PostgresFixture.ApiRole, "blind index read", "SELECT holder_document_blind_index FROM accounts WHERE false" },
        { PostgresFixture.ApiRole, "every column read", "SELECT * FROM accounts WHERE false" },
        { PostgresFixture.WorkerRole, "currency swapped", "UPDATE accounts SET currency = 'EUR' WHERE false" },
        { PostgresFixture.WorkerRole, "event payload forged", "UPDATE outbox_messages SET payload = '{}'::jsonb WHERE false" },
        { PostgresFixture.WorkerRole, "event type forged", "UPDATE outbox_messages SET type = 'Forged' WHERE false" },
        { PostgresFixture.WorkerRole, "event account forged", "UPDATE outbox_messages SET account_id = account_id WHERE false" }
    };

    public static TheoryData<string, string, string> StatementsTheCodeRuns() => new()
    {
        { PostgresFixture.ApiRole, "balance applied", "UPDATE account_balances SET balance = balance, version = version, last_entry_id = last_entry_id, last_recorded_at = last_recorded_at WHERE false" },
        { PostgresFixture.ApiRole, "account columns read", "SELECT id, currency, created_at FROM accounts WHERE false" },
        { PostgresFixture.ApiRole, "account currency joined", "SELECT a.currency FROM accounts AS a JOIN account_balances AS ab ON ab.account_id = a.id WHERE a.id = gen_random_uuid()" },
        { PostgresFixture.WorkerRole, "rewrap batch locked", "SELECT id FROM accounts WHERE false FOR NO KEY UPDATE SKIP LOCKED" },
        { PostgresFixture.WorkerRole, "rewrap batch locked for update", "SELECT id FROM accounts WHERE false FOR UPDATE SKIP LOCKED" },
        { PostgresFixture.WorkerRole, "outbox claimed", "UPDATE outbox_messages SET locked_until = clock_timestamp(), attempts = attempts + 1 WHERE id IN (SELECT id FROM outbox_messages WHERE false FOR UPDATE SKIP LOCKED)" },
        { PostgresFixture.WorkerRole, "outbox marked", "UPDATE outbox_messages SET published_at = clock_timestamp(), locked_until = NULL WHERE false" }
    };

    [DockerTheory]
    [MemberData(nameof(ColumnPrivileges))]
    public async Task Role_HasExactlyThePrivilegeTheCodeNeedsOnEachColumn(
        string role,
        string table,
        string column,
        string privilege,
        bool expected)
    {
        await using var connection = await postgres.AdministrativeSource.OpenConnectionAsync(CancellationToken.None);

        (await HasColumnPrivilegeAsync(connection, role, table, column, privilege))
            .ShouldBe(expected, $"{role} {privilege} on {table}.{column}");
    }

    [DockerTheory]
    [MemberData(nameof(TamperingStatements))]
    public async Task Role_CannotRunAStatementThatReachesBeyondWhatTheCodeNeeds(string role, string attack, string statement)
    {
        await using var connection = await postgres.OpenConnectionAsRoleAsync(role, CancellationToken.None);

        var denied = await Should.ThrowAsync<PostgresException>(() => SqlRunner.ExecuteAsync(connection, statement), attack);

        denied.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege, attack);
    }

    [DockerTheory]
    [MemberData(nameof(StatementsTheCodeRuns))]
    public async Task Role_StillRunsEveryStatementTheCodeIssues(string role, string name, string statement)
    {
        await using var connection = await postgres.OpenConnectionAsRoleAsync(role, CancellationToken.None);

        await Should.NotThrowAsync(() => SqlRunner.ExecuteAsync(connection, statement), name);
    }

    [DockerFact]
    public async Task Accounts_RefuseADocumentWhoseKeyVersionDisagreesWithTheKeyVersionInsideTheBlob()
    {
        await using var connection = await postgres.AdministrativeSource.OpenConnectionAsync(CancellationToken.None);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken.None);

        var blobSaysOne = $"INSERT INTO accounts (id, currency, holder_document_encrypted, holder_document_blind_index, holder_document_key_version) VALUES (gen_random_uuid(), 'BRL', {Blob}, {BlindIndex}, 2)";
        var denied = await Should.ThrowAsync<PostgresException>(() => SqlRunner.ExecuteAsync(connection, blobSaysOne));

        denied.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        denied.ConstraintName.ShouldBe("ck_accounts_holder_document_key_version_matches_payload");
        await transaction.RollbackAsync(CancellationToken.None);
    }

    [DockerFact]
    public async Task Accounts_RefuseADocumentTooShortToCarryAKeyVersion()
    {
        await using var connection = await postgres.AdministrativeSource.OpenConnectionAsync(CancellationToken.None);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken.None);

        var tooShort = $"INSERT INTO accounts (id, currency, holder_document_encrypted, holder_document_blind_index, holder_document_key_version) VALUES (gen_random_uuid(), 'BRL', '\\x01'::bytea, {BlindIndex}, 1)";
        var denied = await Should.ThrowAsync<PostgresException>(() => SqlRunner.ExecuteAsync(connection, tooShort));

        denied.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        await transaction.RollbackAsync(CancellationToken.None);
    }

    [DockerFact]
    public async Task Accounts_AcceptADocumentWhoseKeyVersionMatchesTheBlob()
    {
        await using var connection = await postgres.AdministrativeSource.OpenConnectionAsync(CancellationToken.None);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken.None);

        var matching = $"INSERT INTO accounts (id, currency, holder_document_encrypted, holder_document_blind_index, holder_document_key_version) VALUES (gen_random_uuid(), 'BRL', {Blob}, {BlindIndex}, 1)";

        (await SqlRunner.ExecuteAsync(connection, matching)).ShouldBe(1);
        await transaction.RollbackAsync(CancellationToken.None);
    }

    [DockerFact]
    public async Task ReadOnlyRole_ReadsOnlyTheNonDocumentColumnsOfAccounts()
    {
        await using var connection = await postgres.AdministrativeSource.OpenConnectionAsync(CancellationToken.None);

        foreach (var column in new[] { "id", "currency", "created_at" })
        {
            (await HasColumnPrivilegeAsync(connection, PostgresFixture.ReadOnlyRole, column, "SELECT")).ShouldBeTrue(column);
        }
    }

    [DockerFact]
    public async Task WorkerRole_UpdateOnAccounts_ReachesTheDocumentColumnsTheRewrapNeeds()
    {
        await using var connection = await postgres.OpenConnectionAsRoleAsync(PostgresFixture.WorkerRole, CancellationToken.None);

        var updated = await SqlRunner.ExecuteAsync(
            connection,
            "UPDATE accounts SET holder_document_encrypted = holder_document_encrypted, holder_document_blind_index = holder_document_blind_index, holder_document_key_version = holder_document_key_version WHERE false");

        updated.ShouldBe(0);
    }

    [DockerFact]
    public async Task ApiRole_InsertingAnAccountWithADocument_NeedsNoUpdatePrivilege()
    {
        await using var connection = await postgres.OpenConnectionAsRoleAsync(PostgresFixture.ApiRole, CancellationToken.None);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken.None);

        var inserted = await SqlRunner.ExecuteAsync(
            connection,
            $"INSERT INTO accounts (id, currency, holder_document_encrypted, holder_document_blind_index, holder_document_key_version) VALUES (gen_random_uuid(), 'BRL', {Blob}, {BlindIndex}, 1)");

        inserted.ShouldBe(1);
        await transaction.RollbackAsync(CancellationToken.None);
    }

    [DockerFact]
    public async Task ApiRole_UpdatingAnAccount_IsDeniedSoTheRewrapLivesInTheWorker()
    {
        await using var connection = await postgres.OpenConnectionAsRoleAsync(PostgresFixture.ApiRole, CancellationToken.None);

        var denied = await Should.ThrowAsync<PostgresException>(() =>
            SqlRunner.ExecuteAsync(connection, "UPDATE accounts SET holder_document_key_version = 9 WHERE false"));

        denied.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    [DockerFact]
    public async Task WorkerRole_InsertingOrDeletingAccounts_IsDenied()
    {
        await using var connection = await postgres.OpenConnectionAsRoleAsync(PostgresFixture.WorkerRole, CancellationToken.None);

        var insert = await Should.ThrowAsync<PostgresException>(() =>
            SqlRunner.ExecuteAsync(connection, "INSERT INTO accounts (id, currency) VALUES (gen_random_uuid(), 'BRL')"));
        var delete = await Should.ThrowAsync<PostgresException>(() =>
            SqlRunner.ExecuteAsync(connection, "DELETE FROM accounts WHERE false"));

        insert.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        delete.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    [SuppressMessage("Security", "CA2100",
        Justification = "The privilege and the names come from constants of this file.")]
    private static async Task<bool> HasTablePrivilegeAsync(NpgsqlConnection connection, string role, string table, string privilege)
    {
        await using var command = new NpgsqlCommand("SELECT has_table_privilege(@role, @table, @privilege)", connection);
        command.Parameters.AddWithValue("role", role);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("privilege", privilege);

        return (bool)(await command.ExecuteScalarAsync(CancellationToken.None) ?? false);
    }

    private static Task<bool> HasColumnPrivilegeAsync(NpgsqlConnection connection, string role, string column, string privilege) =>
        HasColumnPrivilegeAsync(connection, role, "accounts", column, privilege);

    private static async Task<bool> HasColumnPrivilegeAsync(
        NpgsqlConnection connection,
        string role,
        string table,
        string column,
        string privilege)
    {
        await using var command = new NpgsqlCommand(
            "SELECT has_column_privilege(@role, @table, @column, @privilege)",
            connection);
        command.Parameters.AddWithValue("role", role);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("column", column);
        command.Parameters.AddWithValue("privilege", privilege);

        return (bool)(await command.ExecuteScalarAsync(CancellationToken.None) ?? false);
    }
}
