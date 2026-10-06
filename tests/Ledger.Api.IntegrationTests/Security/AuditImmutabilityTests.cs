using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application.Audit;
using Ledger.Domain.Accounts;
using Ledger.Infrastructure.Persistence;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Security;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class AuditImmutabilityTests(PostgresFixture postgres)
{
    private static readonly string[] DocumentColumnQueries =
    [
        "SELECT holder_document_encrypted FROM accounts",
        "SELECT holder_document_blind_index FROM accounts",
        "SELECT holder_document_key_version FROM accounts"
    ];

    private static readonly Guid Account = Guid.Parse("0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33");

    public static TheoryData<string, string> ApplicationRoleStatements()
    {
        var data = new TheoryData<string, string>();

        foreach (var role in new[] { PostgresFixture.ApiRole, PostgresFixture.WorkerRole })
        {
            data.Add(role, "UPDATE audit_log SET outcome = 'DENIED'");
            data.Add(role, "DELETE FROM audit_log");
            data.Add(role, "TRUNCATE audit_log");
        }

        return data;
    }

    public static TheoryData<string, string> OwnerStatements() => new()
    {
        { "UPDATE audit_log SET outcome = 'DENIED'", "UPDATE on audit_log is not allowed" },
        { "DELETE FROM audit_log", "DELETE on audit_log is not allowed" },
        { "TRUNCATE audit_log", "TRUNCATE on audit_log is not allowed" }
    };

    private static async Task SeedOneRowAsync(SecurityDatabase database)
    {
        var trail = new PostgresAuditTrail(database.Connections, PostgresSource.Write, new RecordingSecurityTelemetry());

        await trail.RecordAsync(
            AuditEvents.AccountCreated("pix-core", AccountId.From(Account).Value, "corr-immutable"),
            CancellationToken.None);
    }

    [DockerTheory]
    [MemberData(nameof(ApplicationRoleStatements))]
    public async Task ApplicationRoles_CannotUpdateDeleteOrTruncateTheTrail(string role, string statement)
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        await SeedOneRowAsync(database);
        await using var connection = await database.OpenAsRoleAsync(role);

        var denied = await Should.ThrowAsync<PostgresException>(() => SqlRunner.ExecuteAsync(connection, statement));

        denied.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    [DockerTheory]
    [MemberData(nameof(OwnerStatements))]
    public async Task SchemaOwner_IsStoppedByTheTriggerFromChangingTheTrail(string statement, string expectedMessage)
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        await SeedOneRowAsync(database);
        await using var owner = await database.OpenAdministrativeAsync();

        var blocked = await Should.ThrowAsync<PostgresException>(() => SqlRunner.ExecuteAsync(owner, statement));

        blocked.MessageText.ShouldBe(expectedMessage);
        blocked.SqlState.ShouldBe(PostgresErrorCodes.IntegrityConstraintViolation);
        (await SqlRunner.CountAsync(owner, "SELECT count(*) FROM audit_log")).ShouldBe(1);
    }

    [DockerFact]
    public async Task ReadOnlyRole_ReadsTheTrailInFullButNeverTheDocumentColumns()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        await SeedOneRowAsync(database);
        await using var readOnly = await database.OpenAsRoleAsync(PostgresFixture.ReadOnlyRole);

        await using (var trail = new NpgsqlCommand(
                         "SELECT id, recorded_at, event_type, client_id, account_id, correlation_id, outcome, details FROM audit_log",
                         readOnly))
        await using (var reader = await trail.ExecuteReaderAsync(CancellationToken.None))
        {
            (await reader.ReadAsync(CancellationToken.None)).ShouldBeTrue();
        }

        foreach (var query in DocumentColumnQueries)
        {
            var failure = await Should.ThrowAsync<PostgresException>(() => SqlRunner.ScalarAsync(readOnly, query));

            failure.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        }
    }

    [DockerFact]
    public async Task ReadOnlyRole_CannotWriteTheTrail()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        await using var readOnly = await database.OpenAsRoleAsync(PostgresFixture.ReadOnlyRole);

        var denied = await Should.ThrowAsync<PostgresException>(() => SqlRunner.ExecuteAsync(
            readOnly,
            "INSERT INTO audit_log (event_type, client_id, correlation_id, outcome) VALUES ('account.created', 'x', 'y', 'SUCCESS')"));

        denied.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }
}
