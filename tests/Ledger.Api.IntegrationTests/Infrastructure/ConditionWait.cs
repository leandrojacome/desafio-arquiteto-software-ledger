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
