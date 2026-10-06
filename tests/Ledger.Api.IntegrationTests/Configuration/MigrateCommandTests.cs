using System.Diagnostics;
using Ledger.Api.IntegrationTests.Infrastructure;

namespace Ledger.Api.IntegrationTests.Configuration;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class MigrateCommandTests(PostgresFixture postgres)
{
    [DockerFact]
    public async Task TheMigrateCommand_WhenAnotherSessionHoldsTheLockPastTheTimeout_ExitsWithCode2AndAppliesNothing()
    {
        await using var database = await postgres.CreateEmptyDatabaseAsync(CancellationToken.None);
        await using var holder = await MigrationLockHolder.HoldAsync(database);
        var stopwatch = Stopwatch.StartNew();

        var exitCode = await WorkerEntryPoint
            .RunAsync(MigrateArguments(database, "1"))
            .WaitAsync(TimeSpan.FromSeconds(60));

        stopwatch.Stop();

        exitCode.ShouldBe(2);
        stopwatch.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1));
        (await MigrationLockHolder.CountPublicTablesAsync(database)).ShouldBe(0);

        await MigrationLockHolder.ReleaseAsync(holder);

        var afterRelease = await WorkerEntryPoint
            .RunAsync(MigrateArguments(database, "1"))
            .WaitAsync(TimeSpan.FromSeconds(60));

        afterRelease.ShouldBe(0);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("3601")]
    public async Task TheMigrateCommand_WithAnInvalidLockTimeout_ExitsWithCode3BeforeTouchingTheDatabase(string value)
    {
        var arguments = TestConfiguration.ForUnreachablePostgres()
            .Where(pair => pair.Key.StartsWith("Postgres:", StringComparison.Ordinal) && pair.Value is not null)
            .Select(pair => $"--{pair.Key}={pair.Value}")
            .Append($"--Migrations:LockTimeoutSeconds={value}")
            .Append("--environment=Development")
            .Append("--migrate")
            .ToArray();

        var exitCode = await WorkerEntryPoint.RunAsync(arguments).WaitAsync(TimeSpan.FromSeconds(60));

        exitCode.ShouldBe(3);
    }

    private string[] MigrateArguments(EmptyDatabase database, string lockTimeoutSeconds)
    {
        var settings = postgres.ConfigurationWith(new Dictionary<string, string?> { ["Postgres:Database"] = database.Name });

        return
        [
            "--environment=Development",
            .. settings
                .Where(pair => pair.Key.StartsWith("Postgres:", StringComparison.Ordinal) && pair.Value is not null)
                .Select(pair => $"--{pair.Key}={pair.Value}"),
            $"--Migrations:LockTimeoutSeconds={lockTimeoutSeconds}",
            "--migrate"
        ];
    }
}
