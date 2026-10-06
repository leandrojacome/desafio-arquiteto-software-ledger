using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application.Integrity;
using Ledger.Infrastructure.Persistence;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Integrity;

[Collection(MessagingCollectionDefinition.Name)]
[Trait("Category", "Concurrency")]
public sealed class IntegrityLockTests(PostgresFixture postgres)
{
    [DockerFact]
    public async Task WhileAnotherInstanceHoldsTheRecentLock_TheRecentRunIsSkippedButTheFullRunRuns()
    {
        await ConcurrencySettings.RepeatAsync(async () =>
        {
            await using var harness = await IntegrityHarness.CreateAsync(postgres);

            await harness.Ledger.SeedAsync();

            var holder = await harness.Sessions.TryBeginRunAsync(IntegrityMode.Recent, CancellationToken.None);

            holder.ShouldNotBeNull();

            var skipped = await harness.RunAsync(IntegrityMode.Recent);
            var full = await harness.RunAsync(IntegrityMode.Full);

            await holder.DisposeAsync();

            var afterRelease = await harness.RunAsync(IntegrityMode.Recent);

            skipped.Skipped.ShouldBeTrue();
            skipped.AccountsChecked.ShouldBe(0);
            full.Skipped.ShouldBeFalse();
            full.AccountsChecked.ShouldBe(1);
            afterRelease.Skipped.ShouldBeFalse();
        });
    }

    [DockerFact]
    public async Task WhileAnotherInstanceHoldsTheFullLock_TheRecentRunStillRuns()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);

        await harness.Ledger.SeedAsync();

        var holder = await harness.Sessions.TryBeginRunAsync(IntegrityMode.Full, CancellationToken.None);

        holder.ShouldNotBeNull();

        var recent = await harness.RunAsync(IntegrityMode.Recent);
        var full = await harness.RunAsync(IntegrityMode.Full);

        await holder.DisposeAsync();

        recent.Skipped.ShouldBeFalse();
        full.Skipped.ShouldBeTrue();
    }

    [DockerFact]
    public async Task EightRunsStartedTogether_LetExactlyOneInstanceHoldTheRecentLock()
    {
        await ConcurrencySettings.RepeatAsync(async () =>
        {
            await using var harness = await IntegrityHarness.CreateAsync(postgres);

            var attempts = await ParallelGate.RunAsync(8, async _ =>
            {
                var session = await harness.Sessions.TryBeginRunAsync(IntegrityMode.Recent, CancellationToken.None);

                if (session is not null)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(600));
                    await session.DisposeAsync();
                }

                return session is not null;
            });

            attempts.Count(won => won).ShouldBe(1);
        });
    }

    [DockerFact]
    public async Task TheLock_IsReleasedWhenTheBackendIsTerminated()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);

        var holder = await harness.Sessions.TryBeginRunAsync(IntegrityMode.Recent, CancellationToken.None);

        holder.ShouldNotBeNull();

        await TerminateLockHolderAsync(harness);

        var next = await harness.Sessions.TryBeginRunAsync(IntegrityMode.Recent, CancellationToken.None);

        next.ShouldNotBeNull();

        await next.DisposeAsync();
        await holder.DisposeAsync();
    }

    internal static async Task TerminateLockHolderAsync(IntegrityHarness harness)
    {
        await using var admin = await harness.Database.OpenAdministrativeConnectionAsync(CancellationToken.None);
        await using var command = new NpgsqlCommand(
            """
            SELECT pg_terminate_backend(l.pid)
            FROM pg_locks AS l
            WHERE l.locktype = 'advisory' AND l.classid = 727002 AND l.granted
            """,
            admin);

        await command.ExecuteScalarAsync(CancellationToken.None);

        await ConditionWait.UntilAsync(
            async () => await AdvisoryLocksAsync(admin) == 0,
            TimeSpan.FromSeconds(10),
            "the advisory lock to disappear");
    }

    private static async Task<long> AdvisoryLocksAsync(NpgsqlConnection admin)
    {
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND classid = 727002",
            admin);

        return (long)(await command.ExecuteScalarAsync(CancellationToken.None) ?? 0L);
    }
}
