using System.ComponentModel.DataAnnotations;

namespace Ledger.Infrastructure.Persistence;

internal sealed class PostgresSourceOptions
{
    [Required] public string Username { get; init; } = string.Empty;

    [Required] public string Password { get; init; } = string.Empty;

    [Range(1, 500)] public int MaxPoolSize { get; init; } = 5;

    [Range(0, 500)] public int MinPoolSize { get; init; }

    [Range(1, 60)] public int ConnectionTimeoutSeconds { get; init; } = 1;

    [Range(1, 3600)] public int CommandTimeoutSeconds { get; init; } = 2;

    [Range(1, 600_000)] public int? LockTimeoutMs { get; init; }

    [Range(1, 3_600_000)] public int StatementTimeoutMs { get; init; } = 2500;

    [Range(1, 3_600_000)] public int IdleInTransactionTimeoutMs { get; init; } = 5000;

    public bool ReadOnly { get; init; }
}
