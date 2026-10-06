using System.ComponentModel.DataAnnotations;

namespace Ledger.Infrastructure.Persistence;

internal sealed class BalanceReadOptions
{
    public const string SectionName = "Ledger:Balance";

    [Range(1, 60)] public int SettlingWindowSeconds { get; init; } = 5;
}
