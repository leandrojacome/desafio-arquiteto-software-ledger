using System.ComponentModel.DataAnnotations;

namespace Ledger.Worker;

internal sealed class IntegrityOptions
{
    public const string SectionName = "Integrity";

    [Range(1, 60)] public int RecentIntervalMinutes { get; init; } = 5;

    [Range(1, 60)] public int RecentOverlapMinutes { get; init; } = 1;

    [Range(1, 168)] public int FullIntervalHours { get; init; } = 24;

    [Range(100, 20_000)] public int HeadBatchSize { get; init; } = 5000;

    [Range(1, 60)] public int ChainSliceMinutes { get; init; } = 10;
}
