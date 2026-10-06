namespace Ledger.Api.IntegrationTests.Infrastructure;

internal static class BlockedBackends
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    public static Task UntilAnyAsync(PostgresFixture postgres, string description)
    {
        ArgumentNullException.ThrowIfNull(postgres);

        return ConditionWait.UntilAsync(async () => await CountAsync(postgres) > 0, Patience, description);
    }

    private static async Task<long> CountAsync(PostgresFixture postgres)
    {
        await using var command = postgres.AdministrativeSource.CreateCommand(
            "SELECT count(*) FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND pid <> pg_backend_pid()");

        return (long)(await command.ExecuteScalarAsync(CancellationToken.None) ?? 0L);
    }
}
