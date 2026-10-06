namespace Ledger.Api.IntegrationTests.Infrastructure;

internal static class ConditionWait
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(100);

    public static async Task UntilAsync(
        Func<Task<bool>> condition,
        TimeSpan timeout,
        string description,
        TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(condition);

        var interval = pollInterval ?? DefaultPollInterval;
        using var deadline = new CancellationTokenSource(timeout);

        while (true)
        {
            if (await condition())
            {
                return;
            }

            try
            {
                await Task.Delay(interval, deadline.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"Condition not met within {timeout}: {description}");
            }
        }
    }

    public static Task UntilAsync(Func<bool> condition, TimeSpan timeout, string description) =>
        UntilAsync(() => Task.FromResult(condition()), timeout, description);
}

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
