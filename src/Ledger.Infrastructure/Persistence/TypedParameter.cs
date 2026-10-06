using System.Data;
using Dapper;
using Npgsql;
using NpgsqlTypes;

namespace Ledger.Infrastructure.Persistence;

internal sealed class TypedParameter(NpgsqlDbType type, object? value) : SqlMapper.ICustomQueryParameter
{
    public void AddParameter(IDbCommand command, string name)
    {
        var parameter = new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value };

        command.Parameters.Add(parameter);
    }
}
