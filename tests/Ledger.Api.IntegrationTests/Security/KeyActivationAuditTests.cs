using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application.Security;
using Ledger.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ledger.Api.IntegrationTests.Security;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class KeyActivationAuditTests(PostgresFixture postgres)
{
    private static RecordKeyActivationHandler Handler(SecurityDatabase database, int activeVersion)
    {
        var trail = new PostgresAuditTrail(database.Connections, PostgresSource.Worker, new RecordingSecurityTelemetry());

        return new RecordKeyActivationHandler(
            PiiKeys.Provider(activeVersion, 1, 2),
            trail,
            new Uuid7IdGenerator(TimeProvider.System),
            NullLogger<RecordKeyActivationHandler>.Instance);
    }

    [DockerFact]
    public async Task Handle_FirstStartWithAVersion_RecordsItOnce()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);

        var first = await Handler(database, 1).HandleAsync(CancellationToken.None);
        var second = await Handler(database, 1).HandleAsync(CancellationToken.None);

        first.ShouldBeTrue();
        second.ShouldBeFalse();
        await using var admin = await database.OpenAdministrativeAsync();
        (await SqlRunner.CountAsync(admin, "SELECT count(*) FROM audit_log WHERE event_type = 'keys.version_activated'")).ShouldBe(1);
    }

    [DockerFact]
    public async Task Handle_EachNewActiveVersion_IsRecordedOnceMoreWithAPassIdentifier()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);

        await Handler(database, 1).HandleAsync(CancellationToken.None);
        var activated = await Handler(database, 2).HandleAsync(CancellationToken.None);
        var again = await Handler(database, 2).HandleAsync(CancellationToken.None);

        activated.ShouldBeTrue();
        again.ShouldBeFalse();
        await using var admin = await database.OpenAdministrativeAsync();
        (await SqlRunner.CountAsync(admin, "SELECT count(*) FROM audit_log WHERE event_type = 'keys.version_activated'")).ShouldBe(2);
        (await SqlRunner.ScalarAsync(admin, "SELECT string_agg(details ->> 'version', ',' ORDER BY id) FROM audit_log")).ShouldBe("1,2");
        (await SqlRunner.ScalarAsync(admin, "SELECT min(length(correlation_id)) FROM audit_log")).ShouldBe(32);
        (await SqlRunner.ScalarAsync(admin, "SELECT count(DISTINCT client_id) FROM audit_log")).ShouldBe(1L);
    }

    [DockerFact]
    public async Task Handle_TheRowCarriesTheWorkerIdentityAndNoAccount()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);

        await Handler(database, 1).HandleAsync(CancellationToken.None);

        await using var admin = await database.OpenAdministrativeAsync();
        (await SqlRunner.ScalarAsync(admin, "SELECT client_id FROM audit_log")).ShouldBe("ledger-worker");
        (await SqlRunner.ScalarAsync(admin, "SELECT account_id IS NULL FROM audit_log")).ShouldBe(true);
        (await SqlRunner.ScalarAsync(admin, "SELECT outcome FROM audit_log")).ShouldBe("SUCCESS");
    }
}
