using System.Diagnostics;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application.Abstractions;
using Ledger.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Persistence;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Concurrency")]
[Trait("Category", "Integration")]
public sealed class ConcurrentMigrationsTests(PostgresFixture postgres)
{
    private const int Runners = 4;
    private const int LockTimeoutSeconds = 2;

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(120);

    [DockerFact]
    public async Task FourRunnersStartedTogetherOnAnEmptyDatabase_AllSucceed_AndEachScriptIsRecordedOnce()
    {
        var expected = postgres.FirstMigration.AppliedScripts;

        await ConcurrencySettings.RepeatAsync(async () =>
        {
            await using var database = await postgres.CreateEmptyDatabaseAsync(CancellationToken.None);

            var reports = await ParallelGate
                .RunAsync(Runners, _ => database.MigrateAsync(CancellationToken.None))
                .WaitAsync(Patience);

            reports.ShouldAllBe(report => report.Status == MigrationStatus.Succeeded);
            reports.Sum(report => report.AppliedScripts.Count).ShouldBe(expected.Count);
            reports.SelectMany(report => report.AppliedScripts).ShouldBe(expected, ignoreOrder: true);
            (await JournalAsync(database)).ShouldBe(expected, ignoreOrder: true);
        });
    }

    [DockerFact]
    public async Task ALockHeldByAnotherSession_MakesTheRunnerLeaveWithLockTimedOutWithinTheConfiguredTimeout()
    {
        await ConcurrencySettings.RepeatAsync(async () =>
        {
            await using var database = await postgres.CreateEmptyDatabaseAsync(CancellationToken.None);
            await using var holder = await MigrationLockHolder.HoldAsync(database);
            var options = new MigrationOptions { LockTimeoutSeconds = LockTimeoutSeconds };
            var stopwatch = Stopwatch.StartNew();

            var report = await database
                .MigrateAsync(options, NullLogger<MigrationRunner>.Instance, CancellationToken.None)
                .WaitAsync(Patience);

            stopwatch.Stop();

            report.Status.ShouldBe(MigrationStatus.LockTimedOut);
            stopwatch.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(LockTimeoutSeconds));
            stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(LockTimeoutSeconds + 10));
            (await MigrationLockHolder.CountPublicTablesAsync(database)).ShouldBe(0);
        });
    }

    private static async Task<List<string>> JournalAsync(EmptyDatabase database)
    {
        await using var connection = await database.OpenAdministrativeConnectionAsync(CancellationToken.None);
        await using var command = new NpgsqlCommand("SELECT scriptname FROM schemaversions", connection);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        var names = new List<string>();

        while (await reader.ReadAsync(CancellationToken.None))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
