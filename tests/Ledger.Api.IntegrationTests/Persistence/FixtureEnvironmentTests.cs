using Ledger.Api.IntegrationTests.Infrastructure;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Persistence;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class FixtureEnvironmentTests(PostgresFixture postgres)
{
    [DockerFact]
    public async Task TheServer_CommitsSynchronouslyAndAllowsTheConnectionsOfTheCompose()
    {
        (await SettingAsync("synchronous_commit")).ShouldBe("on");
        (await SettingAsync("max_connections")).ShouldBe("200");
    }

    [DockerFact]
    public async Task TheSchema_IsOwnedByTheMigratorRoleThatAppliedIt()
    {
        await using var connection = await postgres.OpenConnectionAsync(CancellationToken.None);
        await using var command = new NpgsqlCommand(
            "SELECT string_agg(DISTINCT tableowner, ',') FROM pg_tables WHERE schemaname = 'public'",
            connection);

        var owners = (string)(await command.ExecuteScalarAsync() ?? string.Empty);

        owners.ShouldBe(PostgresFixture.MigratorRole);
        postgres.FirstMigration.Succeeded.ShouldBeTrue();
    }

    [DockerFact]
    public void TheApplicationSources_UseTheRolesOfTheRealEnvironment()
    {
        postgres.Settings.Sources.Write.Username.ShouldBe(PostgresFixture.ApiRole);
        postgres.Settings.Sources.Balance.Username.ShouldBe(PostgresFixture.ApiRole);
        postgres.Settings.Sources.Statement.Username.ShouldBe(PostgresFixture.ApiRole);
        postgres.Settings.Sources.Worker.Username.ShouldBe(PostgresFixture.WorkerRole);
        postgres.Settings.Sources.Migrator.Username.ShouldBe(PostgresFixture.MigratorRole);
        postgres.Settings.IncludeErrorDetail.ShouldBeFalse();
    }

    [DockerFact]
    public async Task TheApplicationRoles_CannotCreateObjectsInThePublicSchema()
    {
        foreach (var role in new[] { PostgresFixture.ApiRole, PostgresFixture.WorkerRole, PostgresFixture.ReadOnlyRole })
        {
            await using var connection = await postgres.OpenConnectionAsRoleAsync(role, CancellationToken.None);
            await using var command = new NpgsqlCommand("CREATE TABLE intruder (id int)", connection);

            var refused = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

            refused.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        }
    }

    private async Task<string> SettingAsync(string name)
    {
        await using var connection = await postgres.OpenConnectionAsync(CancellationToken.None);
        await using var command = new NpgsqlCommand("SELECT current_setting(@name)", connection);

        command.Parameters.AddWithValue("name", name);

        return (string)(await command.ExecuteScalarAsync() ?? string.Empty);
    }
}
