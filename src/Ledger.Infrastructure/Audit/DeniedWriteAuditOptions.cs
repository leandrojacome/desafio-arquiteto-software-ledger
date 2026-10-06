namespace Ledger.Infrastructure.Audit;

internal sealed class DeniedWriteAuditOptions
{
    public const string SectionName = "Security:Audit:DeniedWrite";
    public const int DefaultCapacity = 10;
    public const int DefaultRefillPerSecond = 1;
    public const int MinValue = 1;
    public const int MaxValue = 1000;

    public int Capacity { get; init; } = DefaultCapacity;

    public int RefillPerSecond { get; init; } = DefaultRefillPerSecond;
}
