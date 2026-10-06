using System.ComponentModel.DataAnnotations;

namespace Ledger.Infrastructure.Persistence;

internal sealed class MigrationOptions
{
    public const string SectionName = "Migrations";

    [Range(1, 3600)] public int LockTimeoutSeconds { get; init; } = 120;
}
