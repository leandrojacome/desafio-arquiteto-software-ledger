using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application.Integrity;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Integrity;

[Collection(MessagingCollectionDefinition.Name)]
[Trait("Category", "Resilience")]
public sealed class IntegrityFailureTests(PostgresFixture postgres)
{
    [DockerFact]
    public async Task ARunThatThrows_ReleasesTheLockAndRecordsNoCompletedRun()
    {
        FaultyIntegritySessions? faulty = null;

        await using var harness = await IntegrityHarness.CreateAsync(
            postgres,
            decorate: inner => faulty = new FaultyIntegritySessions(inner));

        await harness.Ledger.SeedAsync();

        faulty.ShouldNotBeNull();
        faulty.BeforeCheckChain = () => throw new InvalidOperationException("simulated failure in the middle of the run");

        var failure = await Should.ThrowAsync<InvalidOperationException>(
            () => harness.Handler.HandleAsync(IntegrityHarness.CommandFor(IntegrityMode.Recent), CancellationToken.None));

        failure.Message.ShouldContain("simulated failure");
        harness.Telemetry.Runs.ShouldHaveSingleItem().WasFailed.ShouldBeTrue();
        (await harness.Ledger.AuditRowsAsync("integrity.run_completed")).ShouldBeEmpty();

        var next = await harness.Sessions.TryBeginRunAsync(IntegrityMode.Recent, CancellationToken.None);

        next.ShouldNotBeNull();

        await next.DisposeAsync();
    }

    [DockerFact]
    public async Task ARunWhoseConnectionIsTerminatedInTheMiddle_FailsAndTheNextRunCoversTheLostWindow()
    {
        FaultyIntegritySessions? faulty = null;

        await using var harness = await IntegrityHarness.CreateAsync(
            postgres,
            decorate: inner => faulty = new FaultyIntegritySessions(inner));

        var account = await harness.Ledger.SeedAsync();

        await harness.Ledger.TamperStoredBalanceAsync(account.AccountId, 1.00m);

        faulty.ShouldNotBeNull();
        faulty.BeforeCheckHeads = async () =>
        {
            await IntegrityLockTests.TerminateLockHolderAsync(harness);
            faulty.BeforeCheckHeads = null;
        };

        await Should.ThrowAsync<Exception>(
            () => harness.Handler.HandleAsync(IntegrityHarness.CommandFor(IntegrityMode.Recent), CancellationToken.None));

        harness.Telemetry.Runs.ShouldHaveSingleItem().WasFailed.ShouldBeTrue();
        (await harness.Ledger.AuditRowsAsync("integrity.run_completed")).ShouldBeEmpty();

        var recovered = await harness.RunAsync(IntegrityMode.Recent);

        recovered.Skipped.ShouldBeFalse();
        recovered.Violations.ShouldBe(1);
        (await harness.Ledger.AuditRowsAsync("integrity.run_completed")).ShouldHaveSingleItem().Outcome
            .ShouldBe("FAILURE");
    }

    [DockerFact]
    public async Task ARunThatFailsOnTheFirstQuery_AfterAcquiringTheLock_StillFreesItForOtherInstances()
    {
        FaultyIntegritySessions? faulty = null;

        await using var harness = await IntegrityHarness.CreateAsync(
            postgres,
            decorate: inner => faulty = new FaultyIntegritySessions(inner));

        faulty.ShouldNotBeNull();
        faulty.BeforeCheckHeads = () => throw new NpgsqlException("simulated connection failure");

        await harness.Ledger.SeedAsync();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await Should.ThrowAsync<NpgsqlException>(
                () => harness.Handler.HandleAsync(
                    IntegrityHarness.CommandFor(IntegrityMode.Recent),
                    CancellationToken.None));
        }

        faulty.BeforeCheckHeads = null;

        (await harness.RunAsync(IntegrityMode.Recent)).Skipped.ShouldBeFalse();
    }
}
