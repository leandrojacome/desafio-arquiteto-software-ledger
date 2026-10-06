using System.ComponentModel.DataAnnotations;

namespace Ledger.Api.Reads;

internal sealed class StatementOptions
{
    public const string SectionName = "Ledger:Statement";
    public const int LimitCeiling = 200;
    public const int DefaultPageSize = 50;

    [Range(1, LimitCeiling)] public int DefaultLimit { get; init; } = DefaultPageSize;

    [Range(1, LimitCeiling)] public int MaxLimit { get; init; } = LimitCeiling;
}
