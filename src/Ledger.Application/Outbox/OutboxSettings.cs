namespace Ledger.Application.Outbox;

public sealed record OutboxSettings
{
    public OutboxSettings(
        int batchSize,
        TimeSpan lease,
        TimeSpan confirmTimeout,
        int failedAttempts,
        int failedHeadWindow,
        int pendingCap,
        TimeSpan retention,
        int pruneBatchSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(confirmTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lease, confirmTimeout);
        ArgumentOutOfRangeException.ThrowIfLessThan(failedAttempts, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(failedHeadWindow, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pendingCap, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(retention, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(pruneBatchSize, 1);

        BatchSize = batchSize;
        Lease = lease;
        ConfirmTimeout = confirmTimeout;
        FailedAttempts = failedAttempts;
        FailedHeadWindow = failedHeadWindow;
        PendingCap = pendingCap;
        Retention = retention;
        PruneBatchSize = pruneBatchSize;
    }

    public int BatchSize { get; }

    public TimeSpan Lease { get; }

    public TimeSpan ConfirmTimeout { get; }

    public int FailedAttempts { get; }

    public int FailedHeadWindow { get; }

    public int PendingCap { get; }

    public TimeSpan Retention { get; }

    public int PruneBatchSize { get; }
}
