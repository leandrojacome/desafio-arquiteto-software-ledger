using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Infrastructure;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Persistence;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed partial class MigrationTests(PostgresFixture postgres)
{
    private const string ScriptNamespace = "Ledger.Infrastructure.Persistence.Migrations.";
    private const string ScriptSuffix = ".sql";

    [DockerFact]
    public void FirstRun_AppliesEveryEmbeddedScriptFromAnEmptyDatabaseInNumericOrder()
    {
        var expected = EmbeddedScripts().Select(script => script.Name).ToList();

        expected.ShouldNotBeEmpty();
        postgres.FirstMigration.Succeeded.ShouldBeTrue();
        postgres.FirstMigration.AppliedScripts.ShouldBe(expected);
    }

    [DockerFact]
    public async Task FirstRun_CreatesEveryTableTheScriptsDeclare()
    {
        var expected = TablesDeclaredByTheScripts();

        await using var connection = await postgres.OpenConnectionAsync(CancellationToken.None);

        await using var command = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_name <> 'schemaversions' ORDER BY table_name",
            connection);

        var tables = new List<string>();

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        while (await reader.ReadAsync(CancellationToken.None))
        {
            tables.Add(reader.GetString(0));
        }

        expected.ShouldNotBeEmpty();
        tables.ShouldBe(expected, ignoreOrder: true);
    }

    [DockerFact]
    public async Task SecondRun_AppliesNothing()
    {
        var report = await postgres.CreateMigrationRunner().MigrateAsync(CancellationToken.None);

        report.Succeeded.ShouldBeTrue();
        report.AppliedScripts.ShouldBeEmpty();
    }

    [DockerFact]
    public async Task ConcurrentRuns_AreSerializedByTheAdvisoryLockAndApplyNothingNew()
    {
        var runs = Enumerable.Range(0, 4)
            .Select(_ => postgres.CreateMigrationRunner().MigrateAsync(CancellationToken.None));

        var reports = await Task.WhenAll(runs);

        reports.ShouldAllBe(report => report.Succeeded && report.AppliedScripts.Count == 0);
    }

    [DockerFact]
    public async Task LedgerEntries_UpdateAndDelete_AreRejectedByTheTrigger()
    {
        await using var connection = await postgres.OpenConnectionAsync(CancellationToken.None);
        var entryId = await InsertAccountWithEntryAsync(connection);

        var update = await Should.ThrowAsync<PostgresException>(() =>
            ExecuteAsync(connection, "UPDATE ledger_entries SET description = 'changed' WHERE id = @id", entryId));
        var delete = await Should.ThrowAsync<PostgresException>(() =>
            ExecuteAsync(connection, "DELETE FROM ledger_entries WHERE id = @id", entryId));

        update.SqlState.ShouldBe(PostgresErrorCodes.IntegrityConstraintViolation);
        delete.SqlState.ShouldBe(PostgresErrorCodes.IntegrityConstraintViolation);
    }

    [DockerFact]
    public async Task LedgerEntries_Truncate_IsRejectedWithAndWithoutCascade()
    {
        await using var connection = await postgres.OpenConnectionAsync(CancellationToken.None);
        await InsertAccountWithEntryAsync(connection);

        var plain =
            await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, "TRUNCATE ledger_entries"));
        var cascade =
            await Should.ThrowAsync<PostgresException>(() =>
                ExecuteAsync(connection, "TRUNCATE ledger_entries CASCADE"));

        plain.SqlState.ShouldBe(PostgresErrorCodes.FeatureNotSupported);
        cascade.SqlState.ShouldBe(PostgresErrorCodes.IntegrityConstraintViolation);
    }

    [DockerFact]
    public async Task AuditLog_UpdateDeleteAndTruncate_AreRejectedByTheTrigger()
    {
        await using var connection = await postgres.OpenConnectionAsync(CancellationToken.None);
        var auditId = await InsertAuditRowAsync(connection);

        var update = await Should.ThrowAsync<PostgresException>(() =>
            ExecuteAsync(connection, "UPDATE audit_log SET outcome = 'FAILURE' WHERE id = @id", auditId));
        var delete = await Should.ThrowAsync<PostgresException>(() =>
            ExecuteAsync(connection, "DELETE FROM audit_log WHERE id = @id", auditId));
        var truncate = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, "TRUNCATE audit_log"));

        update.SqlState.ShouldBe(PostgresErrorCodes.IntegrityConstraintViolation);
        delete.SqlState.ShouldBe(PostgresErrorCodes.IntegrityConstraintViolation);
        truncate.SqlState.ShouldBe(PostgresErrorCodes.IntegrityConstraintViolation);
    }

    [DockerFact]
    public async Task ApiRole_CannotUpdateOrDeleteLedgerEntries()
    {
        await using var admin = await postgres.OpenConnectionAsync(CancellationToken.None);
        var entryId = await InsertAccountWithEntryAsync(admin);

        await using var api = await postgres.OpenConnectionAsRoleAsync(PostgresFixture.ApiRole, CancellationToken.None);

        var update = await Should.ThrowAsync<PostgresException>(() =>
            ExecuteAsync(api, "UPDATE ledger_entries SET description = 'changed' WHERE id = @id", entryId));
        var delete = await Should.ThrowAsync<PostgresException>(() =>
            ExecuteAsync(api, "DELETE FROM ledger_entries WHERE id = @id", entryId));

        update.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        delete.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    [DockerFact]
    public async Task WorkerRole_CannotInsertLedgerEntries()
    {
        await using var worker =
            await postgres.OpenConnectionAsRoleAsync(PostgresFixture.WorkerRole, CancellationToken.None);

        var insert = await Should.ThrowAsync<PostgresException>(() =>
            ExecuteAsync(worker, "INSERT INTO ledger_entries (id) VALUES (@id)", Guid.NewGuid()));

        insert.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    [DockerFact]
    public async Task ReadOnlyRole_CannotReadTheHolderDocumentColumns()
    {
        await using var readOnly =
            await postgres.OpenConnectionAsRoleAsync(PostgresFixture.ReadOnlyRole, CancellationToken.None);

        var allowed =
            await ExecuteScalarAsync(readOnly, "SELECT count(*) FROM accounts WHERE id <> @id", Guid.NewGuid());
        var denied = await Should.ThrowAsync<PostgresException>(() =>
            ExecuteScalarAsync(readOnly, "SELECT holder_document_encrypted FROM accounts WHERE id <> @id",
                Guid.NewGuid()));

        allowed.ShouldNotBeNull();
        denied.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    private static List<EmbeddedScript> EmbeddedScripts()
    {
        var assembly = InfrastructureAssembly.Reference;

        return assembly
            .GetManifestResourceNames()
            .Where(name => name.StartsWith(ScriptNamespace, StringComparison.Ordinal) &&
                           name.EndsWith(ScriptSuffix, StringComparison.Ordinal))
            .Select(name => new EmbeddedScript(name, NumberOf(name), ReadText(assembly, name)))
            .OrderBy(script => script.Number)
            .ToList();
    }

    private static List<string> TablesDeclaredByTheScripts()
    {
        var tables = new List<string>();

        foreach (var script in EmbeddedScripts())
        {
            tables.AddRange(CreatedTable().Matches(script.Text).Select(match => match.Groups["name"].Value));

            foreach (var dropped in DroppedTable().Matches(script.Text).Select(match => match.Groups["name"].Value))
            {
                tables.Remove(dropped);
            }
        }

        return tables;
    }

    private static int NumberOf(string resourceName)
    {
        var digits = resourceName[ScriptNamespace.Length..].TakeWhile(char.IsAsciiDigit).ToArray();

        return int.Parse(new string(digits), CultureInfo.InvariantCulture);
    }

    private static string ReadText(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName) ??
                           throw new InvalidOperationException($"Embedded resource {resourceName} was not found.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    [GeneratedRegex(@"CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?(?:public\.)?(?<name>[a-z_][a-z0-9_]*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CreatedTable();

    [GeneratedRegex(@"DROP\s+TABLE\s+(?:IF\s+EXISTS\s+)?(?:public\.)?(?<name>[a-z_][a-z0-9_]*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DroppedTable();

    private static async Task<Guid> InsertAccountWithEntryAsync(NpgsqlConnection connection)
    {
        var accountId = Guid.NewGuid();
        var entryId = Guid.NewGuid();

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO accounts (id, currency) VALUES (@account_id, 'BRL');
            INSERT INTO account_balances (account_id, balance, version, last_entry_id, last_recorded_at)
            VALUES (@account_id, 100.00, 1, @entry_id, clock_timestamp());
            INSERT INTO ledger_entries (id, account_id, account_version, type, amount, currency, balance_after,
                                        recorded_at, occurred_at, client_id, correlation_id)
            VALUES (@entry_id, @account_id, 1, 'CREDIT', 100.00, 'BRL', 100.00,
                    clock_timestamp(), clock_timestamp(), 'integration-tests', 'integration-tests-correlation');
            """,
            connection);

        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("entry_id", entryId);

        await command.ExecuteNonQueryAsync(CancellationToken.None);

        return entryId;
    }

    private static async Task<long> InsertAuditRowAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO audit_log (event_type, client_id, correlation_id, outcome)
            VALUES ('account.created', 'integration-tests', 'integration-tests-correlation', 'SUCCESS')
            RETURNING id
            """,
            connection);

        var id = await command.ExecuteScalarAsync(CancellationToken.None);

        return Convert.ToInt64(id, CultureInfo.InvariantCulture);
    }

    private sealed record EmbeddedScript(string Name, int Number, string Text);

    [SuppressMessage("Security", "CA2100",
        Justification = "Test helper: every SQL text passed in is a constant literal written in this file.")]
    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, object? id = null)
    {
        await using var command = new NpgsqlCommand(sql, connection);

        if (id is not null)
        {
            command.Parameters.AddWithValue("id", id);
        }

        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    [SuppressMessage("Security", "CA2100",
        Justification = "Test helper: every SQL text passed in is a constant literal written in this file.")]
    private static async Task<object?> ExecuteScalarAsync(NpgsqlConnection connection, string sql, Guid id)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);

        return await command.ExecuteScalarAsync(CancellationToken.None);
    }
}
