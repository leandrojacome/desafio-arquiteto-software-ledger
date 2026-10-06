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
