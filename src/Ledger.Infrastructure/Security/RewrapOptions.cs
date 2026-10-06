using Ledger.Application.Accounts;

namespace Ledger.Infrastructure.Security;

internal sealed class RewrapOptions
{
    public const int DefaultBatchSize = 500;
    public const int DefaultIdleSeconds = 60;
    public const int MinBatchSize = 1;
    public const int MaxBatchSize = 5000;
    public const int MinIdleSeconds = 5;
    public const int MaxIdleSeconds = 3600;

    public int BatchSize { get; init; } = DefaultBatchSize;

    public int IdleSeconds { get; init; } = DefaultIdleSeconds;

    public RewrapSettings ToSettings() => new(BatchSize, TimeSpan.FromSeconds(IdleSeconds));
}
