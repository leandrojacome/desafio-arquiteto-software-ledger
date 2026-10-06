using Dapper;
using Npgsql;

namespace Ledger.Infrastructure.Persistence;

internal sealed class SchemaVersionReader(IPostgresConnectionFactory connectionFactory)
{
    private const string ReadAppliedScriptsSql = "SELECT scriptname FROM schemaversions;";

    public async Task<int?> ReadCurrentAsync(PostgresSource source, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(source, cancellationToken);

        try
        {
            var scripts = await connection.QueryAsync<string>(
                new CommandDefinition(ReadAppliedScriptsSql, cancellationToken: cancellationToken));

            var highest = SchemaVersion.Highest(scripts);

            return highest == 0 ? null : highest;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return null;
        }
    }
}
