using System.Diagnostics.CodeAnalysis;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Infrastructure.Persistence;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Integrity;

[Collection(MessagingCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class WorkerAuditLogGrantTests(PostgresFixture postgres)
{
    [DockerFact]
    public async Task TheWorkerRole_ReadsExactlyTheFiveColumnsOfTheCheckpoint_AndNoOther()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);
        await using var admin = await harness.Database.OpenAdministrativeConnectionAsync(CancellationToken.None);

        foreach (var column in new[] { "id", "recorded_at", "event_type", "outcome", "details" })
        {
            (await ColumnPrivilegeAsync(admin, column)).ShouldBe(true, column);
        }

        foreach (var column in new[] { "client_id", "account_id", "correlation_id" })
        {
            (await ColumnPrivilegeAsync(admin, column)).ShouldBe(false, column);
        }

        (await ScalarAsync(admin, "SELECT has_table_privilege('ledger_worker', 'audit_log', 'UPDATE')")).ShouldBe(false);
        (await ScalarAsync(admin, "SELECT has_table_privilege('ledger_worker', 'audit_log', 'DELETE')")).ShouldBe(false);
        (await ScalarAsync(admin, "SELECT has_table_privilege('ledger_worker', 'audit_log', 'INSERT')")).ShouldBe(true);
    }

    [DockerFact]
    public async Task TheWorkerRole_InsertsWithReturningIdAndReadsTheLastRunWithTheCheckpointQuery()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);
        await using var worker = new NpgsqlConnection(
            PostgresConnectionString.Build(harness.Database.Settings, PostgresSource.Worker));

        await worker.OpenAsync(CancellationToken.None);

        await using (var insert = new NpgsqlCommand(
                         """
                         INSERT INTO audit_log (event_type, client_id, account_id, correlation_id, outcome, details)
                         VALUES ('integrity.run_completed', 'ledger-worker', NULL, 'run', 'SUCCESS', '{"mode": "RECENT"}')
                         RETURNING id
                         """,
                         worker))
        {
            (await insert.ExecuteScalarAsync(CancellationToken.None)).ShouldBeOfType<long>();
        }

        await using var read = new NpgsqlCommand(
            """
            SELECT a.recorded_at, a.outcome, a.details
            FROM audit_log AS a
            WHERE a.event_type = 'integrity.run_completed'
              AND a.details ->> 'mode' = 'RECENT'
            ORDER BY a.recorded_at DESC
            LIMIT 1
            """,
            worker);
        await using var reader = await read.ExecuteReaderAsync(CancellationToken.None);

        (await reader.ReadAsync(CancellationToken.None)).ShouldBeTrue();
        reader.GetString(1).ShouldBe("SUCCESS");
    }

    [DockerFact]
    [SuppressMessage("Security", "CA2100", Justification = "The statements are literals.")]
    public async Task TheWorkerRole_IsDeniedWhenItReadsTheColumnsOutsideTheCheckpoint()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);
        await using var worker = new NpgsqlConnection(
            PostgresConnectionString.Build(harness.Database.Settings, PostgresSource.Worker));

        await worker.OpenAsync(CancellationToken.None);

        foreach (var query in new[]
                 {
                     "SELECT client_id FROM audit_log LIMIT 1",
                     "SELECT account_id FROM audit_log LIMIT 1",
                     "SELECT correlation_id FROM audit_log LIMIT 1"
                 })
        {
            await using var command = new NpgsqlCommand(query, worker);

            var denied = await Should.ThrowAsync<PostgresException>(
                () => command.ExecuteScalarAsync(CancellationToken.None));

            denied.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        }
    }

    private static async Task<object?> ColumnPrivilegeAsync(NpgsqlConnection connection, string column)
    {
        await using var command = new NpgsqlCommand(
            "SELECT has_column_privilege('ledger_worker', 'audit_log', @column, 'SELECT')",
            connection);

        command.Parameters.AddWithValue("column", column);

        return await command.ExecuteScalarAsync(CancellationToken.None);
    }

    [SuppressMessage("Security", "CA2100", Justification = "Callers pass literal SQL.")]
    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);

        return await command.ExecuteScalarAsync(CancellationToken.None);
    }
}
