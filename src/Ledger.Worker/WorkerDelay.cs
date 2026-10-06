namespace Ledger.Worker;

internal static class WorkerDelay
{
    public static async Task<bool> WaitAsync(
        TimeSpan delay,
        TimeProvider timeProvider,
        CancellationToken stoppingToken)
    {
        if (delay <= TimeSpan.Zero)
        {
            return !stoppingToken.IsCancellationRequested;
        }

        try
        {
            await Task.Delay(delay, timeProvider, stoppingToken);

            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
