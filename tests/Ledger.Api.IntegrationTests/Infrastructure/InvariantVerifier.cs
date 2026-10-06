using System.Diagnostics.CodeAnalysis;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Infrastructure;

internal static class InvariantVerifier
{
    private const string ResourcePrefix = "Ledger.Api.IntegrationTests.Invariants";

    public const string BalanceEqualsSumOfEntries = "balance-equals-sum-of-entries";
    public const string BalanceMatchesLastEntryAndFloor = "balance-matches-last-entry-and-floor";
    public const string BalanceAfterChainCloses = "balance-after-chain-closes";
    public const string VersionAndRecordedAtSequence = "version-and-recorded-at-sequence";

    private static readonly string[] Checks =
    [
        BalanceEqualsSumOfEntries,
        BalanceMatchesLastEntryAndFloor,
        BalanceAfterChainCloses,
        VersionAndRecordedAtSequence
    ];

    public static async Task<IReadOnlyDictionary<string, int>> ViolationsAsync(NpgsqlDataSource dataSource,
        Guid accountId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await ViolationsAsync(connection, accountId, cancellationToken);
    }

    private static async Task<IReadOnlyDictionary<string, int>> ViolationsAsync(NpgsqlConnection connection,
        Guid accountId, CancellationToken cancellationToken)
    {
        var violations = new Dictionary<string, int>();

        foreach (var check in Checks)
        {
            var count = await CountRowsAsync(connection, check, accountId, cancellationToken);

            if (count > 0)
            {
                violations[check] = count;
            }
        }

        return violations;
    }

    public static async Task AssertAccountIsConsistentAsync(NpgsqlDataSource dataSource, Guid accountId,
        CancellationToken cancellationToken)
    {
        var violations = await ViolationsAsync(dataSource, accountId, cancellationToken);

        violations.ShouldBeEmpty($"Account {accountId} violates: {string.Join(", ", violations.Keys)}");
    }

    [SuppressMessage("Security", "CA2100",
        Justification = "The SQL text is an embedded resource of this assembly, never user input.")]
    private static async Task<int> CountRowsAsync(NpgsqlConnection connection, string check, Guid accountId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(ReadSql(check), connection);

        command.Parameters.AddWithValue("account_id", accountId);

        var rows = 0;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows++;
        }

        return rows;
    }

    private static string ReadSql(string check)
    {
        var assembly = typeof(InvariantVerifier).Assembly;
        var resourceName = $"{ResourcePrefix}.{check}.sql";

        using var stream = assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException(
                               $"Embedded resource {resourceName} was not found in {assembly.GetName().Name}.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
