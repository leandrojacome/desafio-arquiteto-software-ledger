using System.Text.Json;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application.Abstractions;
using Ledger.Application.Audit;
using Ledger.Domain.Accounts;
using Ledger.Infrastructure.Persistence;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Security;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresAuditTrailTests(PostgresFixture postgres)
{
    private const string AccountText = "0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33";
    private const string PassId = "0192b7c281aa7e04b1d56f0c2a9e8d33";

    private static AccountId Account => AccountId.From(Guid.Parse(AccountText)).Value;

    private static PostgresAuditTrail ApiTrail(SecurityDatabase database, ISecurityTelemetry telemetry) =>
        new(database.Connections, PostgresSource.Write, telemetry);

    private static PostgresAuditTrail WorkerTrail(SecurityDatabase database, ISecurityTelemetry telemetry) =>
        new(database.Connections, PostgresSource.Worker, telemetry);

    private static async Task<AuditRow> ReadSingleAsync(NpgsqlConnection admin, string eventType)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT event_type, client_id, account_id, correlation_id, outcome, details::text, pg_typeof(details)::text, recorded_at
            FROM audit_log
            WHERE event_type = @event_type
            """,
            admin);
        command.Parameters.AddWithValue("event_type", eventType);

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        (await reader.ReadAsync(CancellationToken.None)).ShouldBeTrue($"no audit row of type {eventType}");

        return new AuditRow(
            reader.GetString(0),
            reader.GetString(1),
            await reader.IsDBNullAsync(2) ? null : reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            await reader.GetFieldValueAsync<DateTimeOffset>(7));
    }

    private static string Canonical(string json)
    {
        using var document = JsonDocument.Parse(json);

        return JsonSerializer.Serialize(document.RootElement.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToDictionary(property => property.Name, property => property.Value.ToString()));
    }

    [DockerFact]
    public async Task RecordAsync_AccountCreated_IsStoredWithTheContractColumns()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        var telemetry = new RecordingSecurityTelemetry();

        await ApiTrail(database, telemetry).RecordAsync(
            AuditEvents.AccountCreated("pix-core", Account, "corr-account"),
            CancellationToken.None);

        await using var admin = await database.OpenAdministrativeAsync();
        var row = await ReadSingleAsync(admin, "account.created");
        row.ClientId.ShouldBe("pix-core");
        row.AccountId.ShouldBe(Account.Value);
        row.CorrelationId.ShouldBe("corr-account");
        row.Outcome.ShouldBe("SUCCESS");
        row.DetailsType.ShouldBe("jsonb");
        Canonical(row.Details).ShouldBe(Canonical("{}"));
        row.RecordedAt.ShouldBeGreaterThan(TimeProvider.System.GetUtcNow().AddMinutes(-5));
        telemetry.Recorded.ShouldBe([("account.created", "SUCCESS")]);
    }

    [DockerFact]
    public async Task RecordAsync_AuthorizationDenied_StoresTheRouteTemplateTheScopeAndTheReason()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        var denial = AuditEvents.AuthorizationDenied(
            "reader-only",
            Account,
            "corr-denied",
            "POST /v1/accounts/{accountId}/entries",
            DeniedWriteReason.InsufficientScope);

        await ApiTrail(database, new RecordingSecurityTelemetry()).RecordAsync(denial, CancellationToken.None);

        await using var admin = await database.OpenAdministrativeAsync();
        var row = await ReadSingleAsync(admin, "authorization.denied_write");
        row.Outcome.ShouldBe("DENIED");
        row.ClientId.ShouldBe("reader-only");
        row.AccountId.ShouldBe(Account.Value);
        Canonical(row.Details).ShouldBe(Canonical(
            """{"route":"POST /v1/accounts/{accountId}/entries","requiredScope":"ledger.write","reason":"insufficient_scope"}"""));
    }

    [DockerFact]
    public async Task RecordAsync_EventWithoutAnAccount_StoresANullAccountId()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        var denial = AuditEvents.AuthorizationDenied(
            "client-without-account",
            null,
            "corr-null",
            "POST /v1/accounts",
            DeniedWriteReason.NotProvisioningClient);

        await ApiTrail(database, new RecordingSecurityTelemetry()).RecordAsync(denial, CancellationToken.None);

        await using var admin = await database.OpenAdministrativeAsync();
        var row = await ReadSingleAsync(admin, "authorization.denied_write");
        row.AccountId.ShouldBeNull();
        Canonical(row.Details).ShouldContain("not_provisioning_client", Case.Sensitive);
    }

    [DockerFact]
    public async Task RecordAsync_WorkerEvents_AreStoredByTheWorkerRole()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        var trail = WorkerTrail(database, new RecordingSecurityTelemetry());

        await trail.RecordAsync(AuditEvents.PiiDecrypted(PassId, 500), CancellationToken.None);
        await trail.RecordAsync(AuditEvents.PiiRewrapped(PassId, 1, 2, 500, 0), CancellationToken.None);
        await trail.RecordAsync(AuditEvents.KeysVersionActivated(PassId, 2), CancellationToken.None);

        await using var admin = await database.OpenAdministrativeAsync();
        var decrypted = await ReadSingleAsync(admin, "pii.decrypted");
        var rewrapped = await ReadSingleAsync(admin, "pii.rewrapped");
        var activated = await ReadSingleAsync(admin, "keys.version_activated");

        foreach (var row in new[] { decrypted, rewrapped, activated })
        {
            row.ClientId.ShouldBe("ledger-worker");
            row.AccountId.ShouldBeNull();
            row.CorrelationId.ShouldBe(PassId);
            row.Outcome.ShouldBe("SUCCESS");
            row.DetailsType.ShouldBe("jsonb");
        }

        Canonical(decrypted.Details).ShouldBe(Canonical("""{"purpose":"rewrap","accounts":500}"""));
        Canonical(rewrapped.Details).ShouldBe(Canonical("""{"fromVersion":1,"toVersion":2,"accounts":500,"failed":0}"""));
        Canonical(activated.Details).ShouldBe(Canonical("""{"version":2}"""));
    }

    [DockerFact]
    public async Task ScopedTrail_RolledBackTransaction_LeavesNoRowAndCommittedOneKeepsIt()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        await using var api = await database.OpenAsRoleAsync(PostgresFixture.ApiRole);
        await using var admin = await database.OpenAdministrativeAsync();

        await using (var rolledBack = await api.BeginTransactionAsync(CancellationToken.None))
        {
            await new PostgresScopedAuditTrail(api, rolledBack, new RecordingSecurityTelemetry()).RecordAsync(
                AuditEvents.AccountCreated("pix-core", Account, "corr-rollback"),
                CancellationToken.None);
            await rolledBack.RollbackAsync(CancellationToken.None);
        }

        (await SqlRunner.CountAsync(admin, "SELECT count(*) FROM audit_log")).ShouldBe(0);

        await using (var committed = await api.BeginTransactionAsync(CancellationToken.None))
        {
            await new PostgresScopedAuditTrail(api, committed, new RecordingSecurityTelemetry()).RecordAsync(
                AuditEvents.AccountCreated("pix-core", Account, "corr-commit"),
                CancellationToken.None);
            await committed.CommitAsync(CancellationToken.None);
        }

        (await SqlRunner.CountAsync(admin, "SELECT count(*) FROM audit_log")).ShouldBe(1);
    }

    [DockerFact]
    public async Task ScopedTrail_RowIsInvisibleOutsideTheTransactionUntilItCommits()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        await using var api = await database.OpenAsRoleAsync(PostgresFixture.ApiRole);
        await using var admin = await database.OpenAdministrativeAsync();
        await using var transaction = await api.BeginTransactionAsync(CancellationToken.None);

        await new PostgresScopedAuditTrail(api, transaction, new RecordingSecurityTelemetry()).RecordAsync(
            AuditEvents.AccountCreated("pix-core", Account, "corr-invisible"),
            CancellationToken.None);

        (await SqlRunner.CountAsync(admin, "SELECT count(*) FROM audit_log")).ShouldBe(0);
        await transaction.CommitAsync(CancellationToken.None);
        (await SqlRunner.CountAsync(admin, "SELECT count(*) FROM audit_log")).ShouldBe(1);
    }

    [DockerFact]
    public async Task ContainsAsync_WorkerRole_FindsTheVersionOnlyAfterItWasRecorded()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        var trail = WorkerTrail(database, new RecordingSecurityTelemetry());

        (await trail.ContainsAsync("pii.rewrapped", "toVersion", "2", CancellationToken.None)).ShouldBeFalse();
        (await trail.ContainsAsync("keys.version_activated", "version", "2", CancellationToken.None)).ShouldBeFalse();

        await trail.RecordAsync(AuditEvents.PiiRewrapped(PassId, 1, 2, 10, 0), CancellationToken.None);

        (await trail.ContainsAsync("pii.rewrapped", "toVersion", "2", CancellationToken.None)).ShouldBeTrue();
        (await trail.ContainsAsync("pii.rewrapped", "toVersion", "3", CancellationToken.None)).ShouldBeFalse();
        (await trail.ContainsAsync("keys.version_activated", "version", "2", CancellationToken.None)).ShouldBeFalse();
    }

    [DockerFact]
    public async Task ContainsAsync_ApiRole_IsDeniedBecauseOnlyTheWorkerMayReadTheTrail()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        var trail = ApiTrail(database, new RecordingSecurityTelemetry());

        var denied = await Should.ThrowAsync<PostgresException>(() =>
            trail.ContainsAsync("pii.rewrapped", "toVersion", "2", CancellationToken.None));

        denied.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    [DockerFact]
    public async Task ContainsAsync_WorkerRole_CannotReachTheClientIdColumn()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        await using var worker = await database.OpenAsRoleAsync(PostgresFixture.WorkerRole);
        await using var command = new NpgsqlCommand("SELECT client_id FROM audit_log", worker);

        var denied = await Should.ThrowAsync<PostgresException>(() => command.ExecuteScalarAsync(CancellationToken.None));

        denied.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    [DockerFact]
    public async Task RecordAsync_FailedInsert_DoesNotReportTheEvent()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        var telemetry = new RecordingSecurityTelemetry();
        var tooLong = AuditEvents.AccountCreated(new string('c', 200), Account, "corr");

        await Should.ThrowAsync<PostgresException>(() =>
            ApiTrail(database, telemetry).RecordAsync(tooLong, CancellationToken.None));

        telemetry.Recorded.ShouldBeEmpty();
    }

    [DockerFact]
    public async Task RecordAsync_TrailAcceptsTheFiveCatalogTypesUnderTheOutcomeConstraint()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        var api = ApiTrail(database, new RecordingSecurityTelemetry());
        var worker = WorkerTrail(database, new RecordingSecurityTelemetry());

        await api.RecordAsync(AuditEvents.AccountCreated("c", Account, "1"), CancellationToken.None);
        await api.RecordAsync(
            AuditEvents.AuthorizationDenied("c", null, "2", "DELETE /v1/accounts", DeniedWriteReason.InsufficientScope),
            CancellationToken.None);
        await worker.RecordAsync(AuditEvents.PiiDecrypted(PassId, 1), CancellationToken.None);
        await worker.RecordAsync(AuditEvents.PiiRewrapped(PassId, 1, 2, 1, 0), CancellationToken.None);
        await worker.RecordAsync(AuditEvents.KeysVersionActivated(PassId, 3), CancellationToken.None);

        await using var admin = await database.OpenAdministrativeAsync();
        (await SqlRunner.CountAsync(admin, "SELECT count(*) FROM audit_log")).ShouldBe(5);
    }

    private sealed record AuditRow(
        string EventType,
        string ClientId,
        Guid? AccountId,
        string CorrelationId,
        string Outcome,
        string Details,
        string DetailsType,
        DateTimeOffset RecordedAt);
}
