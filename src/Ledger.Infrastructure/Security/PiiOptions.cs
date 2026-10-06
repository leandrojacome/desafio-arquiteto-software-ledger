using System.Diagnostics.CodeAnalysis;
using Ledger.Application.Accounts;
using Ledger.Application.Security;

namespace Ledger.Infrastructure.Security;

internal sealed class PiiOptions
{
    public const string SectionName = "Security:Pii";
    public const int DefaultReloadMinutes = 10;
    public const int MinReloadMinutes = 1;
    public const int MaxReloadMinutes = 1440;

    public PiiProvider Provider { get; init; } = PiiProvider.Configuration;

    public int ActiveKeyVersion { get; init; } = 1;

    public Dictionary<string, KeySetOptions> KeySets { get; init; } = [];

    public string? Directory { get; init; }

    public int ReloadMinutes { get; init; } = DefaultReloadMinutes;

    public RewrapOptions Rewrap { get; init; } = new();
}

[SuppressMessage("Design", "CA1008",
    Justification = "A zero member would let an unset provider pass for a valid one; the default must stay outside the defined values.")]
internal enum PiiProvider
{
    Directory = 1,
    Configuration = 2
}

internal sealed class KeySetOptions
{
    [Sensitive] public string? EncryptionKey { get; init; }

    [Sensitive] public string? BlindIndexKey { get; init; }
}

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
