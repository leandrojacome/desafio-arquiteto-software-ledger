using System.Diagnostics.CodeAnalysis;
using Ledger.Api.IntegrationTests.Infrastructure;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Persistence;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class RolePrivilegesTests(PostgresFixture postgres)
{
    public static TheoryData<string> ApplicationRoles => new()
    {
        PostgresFixture.ApiRole, PostgresFixture.WorkerRole, PostgresFixture.ReadOnlyRole
    };

    [DockerTheory]
    [MemberData(nameof(ApplicationRoles))]
    public async Task ApplicationRoles_CanReadTheSchemaVersionJournal(string role)
    {
        await using var connection = await postgres.OpenConnectionAsRoleAsync(role, CancellationToken.None);

        var count = await ScalarAsync(connection, "SELECT count(*) FROM schemaversions");

        Convert.ToInt64(count, System.Globalization.CultureInfo.InvariantCulture).ShouldBeGreaterThanOrEqualTo(3);
    }

    [DockerFact]
    public async Task WorkerRole_CanReadTheAuditLogColumnsOfTheIntegrityCheckpoint()
    {
        await using var worker =
            await postgres.OpenConnectionAsRoleAsync(PostgresFixture.WorkerRole, CancellationToken.None);

        var allowed = await ScalarAsync(worker,
            "SELECT count(id) + count(recorded_at) + count(event_type) + count(outcome) + count(details) FROM audit_log");

        allowed.ShouldNotBeNull();
    }

    [DockerTheory]
    [InlineData("client_id")]
    [InlineData("account_id")]
    [InlineData("correlation_id")]
    public async Task WorkerRole_CannotReadTheOtherAuditLogColumns(string column)
    {
        await using var worker =
            await postgres.OpenConnectionAsRoleAsync(PostgresFixture.WorkerRole, CancellationToken.None);

        var denied =
            await Should.ThrowAsync<PostgresException>(() =>
                ScalarAsync(worker, $"SELECT count({column}) FROM audit_log"));

        denied.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    [DockerFact]
    public async Task ApiRole_CannotReadTheAuditLog()
    {
        await using var api = await postgres.OpenConnectionAsRoleAsync(PostgresFixture.ApiRole, CancellationToken.None);

        var denied =
            await Should.ThrowAsync<PostgresException>(() => ScalarAsync(api, "SELECT count(id) FROM audit_log"));

        denied.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    [SuppressMessage("Security", "CA2100",
        Justification = "Test helper: every SQL text passed in is built from constants written in this file.")]
    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);

        return await command.ExecuteScalarAsync(CancellationToken.None);
    }
}
