using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application.Audit;
using Ledger.Domain.Accounts;
using Ledger.Infrastructure.Audit;
using Ledger.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Security;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class DeniedWriteAuditIntegrationTests(PostgresFixture postgres)
{
    private const string Route = "POST /v1/accounts/{accountId}/entries";

    private static readonly AccountId Account = AccountId.From(Guid.Parse("0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33")).Value;

    private static DeniedWriteAuditor Auditor(
        SecurityDatabase database,
        RecordingSecurityTelemetry telemetry,
        FakeTimeProvider time)
    {
        var trail = new PostgresAuditTrail(database.Connections, PostgresSource.Write, telemetry);

        return new DeniedWriteAuditor(
            trail,
            telemetry,
            Options.Create(new DeniedWriteAuditOptions()),
            time,
            NullLogger<DeniedWriteAuditor>.Instance);
    }

    private static void Deny(DeniedWriteAuditor auditor, string client)
    {
        auditor.Record(client, Account, "corr", Route, DeniedWriteReason.InsufficientScope);
    }

    private static async Task<long> CountAsync(NpgsqlConnection admin, string? client = null)
    {
        if (client is null)
        {
            return await SqlRunner.CountAsync(
                admin,
                "SELECT count(*) FROM audit_log WHERE event_type = 'authorization.denied_write'");
        }

        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM audit_log WHERE event_type = 'authorization.denied_write' AND client_id = @client",
            admin);
        command.Parameters.AddWithValue("client", client);

        return Convert.ToInt64(
            await command.ExecuteScalarAsync(CancellationToken.None),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task WaitUntilIdleAsync(DeniedWriteAuditor auditor)
    {
        await ConditionWait.UntilAsync(() => auditor.PendingWrites == 0, TimeSpan.FromSeconds(30), "denied writes finished");
    }

    [DockerFact]
    public async Task Record_StoresTheDenialWithTheTemplateRouteAndNothingElseSensitive()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        var telemetry = new RecordingSecurityTelemetry();
        var auditor = Auditor(database, telemetry, new FakeTimeProvider());

        Deny(auditor, "reader-only");
        await WaitUntilIdleAsync(auditor);

        await using var admin = await database.OpenAdministrativeAsync();
        (await SqlRunner.ScalarAsync(admin, "SELECT details ->> 'route' FROM audit_log")).ShouldBe(Route);
        (await SqlRunner.ScalarAsync(admin, "SELECT details ->> 'requiredScope' FROM audit_log")).ShouldBe("ledger.write");
        (await SqlRunner.ScalarAsync(admin, "SELECT details ->> 'reason' FROM audit_log")).ShouldBe("insufficient_scope");
        (await SqlRunner.ScalarAsync(admin, "SELECT client_id FROM audit_log")).ShouldBe("reader-only");
        (await SqlRunner.ScalarAsync(admin, "SELECT outcome FROM audit_log")).ShouldBe("DENIED");
        telemetry.Recorded.ShouldBe([("authorization.denied_write", "DENIED")]);
        telemetry.Skipped.ShouldBeEmpty();
    }

    [DockerFact]
    [Trait("Category", "Concurrency")]
    public async Task Record_HundredDenialsInOneInstantFromEightCallers_LeaveOnlyTheBurstOfTheCap()
    {
        await ConcurrencySettings.RepeatAsync(async () =>
        {
            await using var database = await SecurityDatabase.CreateAsync(postgres);
            var telemetry = new RecordingSecurityTelemetry();
            var auditor = Auditor(database, telemetry, new FakeTimeProvider());

            await ParallelGate.RunAsync(
                8,
                _ =>
                {
                    for (var count = 0; count < 13; count++)
                    {
                        Deny(auditor, "noisy-client");
                    }

                    return Task.FromResult(0);
                });
            await WaitUntilIdleAsync(auditor);

            await using var admin = await database.OpenAdministrativeAsync();
            (await CountAsync(admin)).ShouldBe(10);
            telemetry.Skipped.Count(reason => reason == "rate_capped").ShouldBe((8 * 13) - 10);
            telemetry.Skipped.ShouldAllBe(reason => reason == "rate_capped");
        });
    }

    [DockerFact]
    public async Task Record_ASecondCallerInTheSameInstant_RecordsItsOwnTen()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        var telemetry = new RecordingSecurityTelemetry();
        var auditor = Auditor(database, telemetry, new FakeTimeProvider());

        for (var count = 0; count < 100; count++)
        {
            Deny(auditor, "noisy-client");
        }

        for (var count = 0; count < 10; count++)
        {
            Deny(auditor, "quiet-client");
        }

        await WaitUntilIdleAsync(auditor);

        await using var admin = await database.OpenAdministrativeAsync();
        (await CountAsync(admin, "noisy-client")).ShouldBe(10);
        (await CountAsync(admin, "quiet-client")).ShouldBe(10);
        telemetry.Skipped.Count.ShouldBe(90);
    }

    [DockerFact]
    public async Task Record_DatabaseUnavailable_NeverThrowsAndCountsTheFailedWrite()
    {
        var database = await SecurityDatabase.CreateAsync(postgres);
        var telemetry = new RecordingSecurityTelemetry();
        var auditor = Auditor(database, telemetry, new FakeTimeProvider());
        await database.DisposeAsync();

        Should.NotThrow(() => Deny(auditor, "any-client"));
        await WaitUntilIdleAsync(auditor);

        telemetry.Skipped.ShouldBe(["write_failed"]);
        telemetry.Recorded.ShouldBeEmpty();
    }
}
