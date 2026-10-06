using System.ComponentModel.DataAnnotations;
using Ledger.Application.Outbox;

namespace Ledger.Infrastructure.Messaging;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    [Range(1, 1000)] public int BatchSize { get; init; } = 200;

    [Range(10, 5000)] public int IdlePollMs { get; init; } = 200;

    [Range(5, 600)] public int LeaseSeconds { get; init; } = 30;

    [Range(1, 60)] public int ConfirmTimeoutSeconds { get; init; } = 5;

    [Range(1, 365)] public int RetentionDays { get; init; } = 7;

    [Range(1, 1440)] public int PruneIntervalMinutes { get; init; } = 10;

    [Range(100, 50_000)] public int PruneBatchSize { get; init; } = 5000;

    [Range(1, 300)] public int MeasureIntervalSeconds { get; init; } = 10;

    [Range(1000, 10_000_000)] public int PendingCap { get; init; } = 1_000_000;

    [Range(2, 100)] public int FailedAttempts { get; init; } = 5;

    [Range(10, 10_000)] public int FailedHeadWindow { get; init; } = 1000;

    public OutboxSettings ToSettings()
    {
        return new OutboxSettings(
            BatchSize,
            TimeSpan.FromSeconds(LeaseSeconds),
            TimeSpan.FromSeconds(ConfirmTimeoutSeconds),
            FailedAttempts,
            FailedHeadWindow,
            PendingCap,
            TimeSpan.FromDays(RetentionDays),
            PruneBatchSize);
    }
}
