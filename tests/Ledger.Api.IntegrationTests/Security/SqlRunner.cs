using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Security;

internal static class SqlRunner
{
    [SuppressMessage("Security", "CA2100",
        Justification = "Test helper: every SQL text passed in is a constant written in the tests.")]
    public static async Task<int> ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);

        return await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    [SuppressMessage("Security", "CA2100",
        Justification = "Test helper: every SQL text passed in is a constant written in the tests.")]
    public static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);

        return await command.ExecuteScalarAsync(CancellationToken.None);
    }

    public static async Task<long> CountAsync(NpgsqlConnection connection, string sql)
    {
        return Convert.ToInt64(await ScalarAsync(connection, sql), CultureInfo.InvariantCulture);
    }
}
