using System.Data;
using Dapper;
using Npgsql;
using NpgsqlTypes;

namespace Ledger.Infrastructure.Persistence;

internal sealed class SqlParameters
{
    private readonly DynamicParameters _parameters = new();

    public SqlParameters Uuid(string name, Guid? value) => Add(name, NpgsqlDbType.Uuid, value);

    public SqlParameters Numeric(string name, decimal? value) => Add(name, NpgsqlDbType.Numeric, value);

    public SqlParameters Bigint(string name, long? value) => Add(name, NpgsqlDbType.Bigint, value);

    public SqlParameters Integer(string name, int? value) => Add(name, NpgsqlDbType.Integer, value);

    public SqlParameters Smallint(string name, short? value) => Add(name, NpgsqlDbType.Smallint, value);

    public SqlParameters Char(string name, string? value) => Add(name, NpgsqlDbType.Char, value);

    public SqlParameters Varchar(string name, string? value) => Add(name, NpgsqlDbType.Varchar, value);

    public SqlParameters Bytea(string name, byte[]? value) => Add(name, NpgsqlDbType.Bytea, value);

    public SqlParameters Jsonb(string name, string? value) => Add(name, NpgsqlDbType.Jsonb, value);

    public SqlParameters TimestampTz(string name, DateTimeOffset? value) =>
        Add(name, NpgsqlDbType.TimestampTz, value?.UtcDateTime);

    public DynamicParameters Build() => _parameters;

    private SqlParameters Add(string name, NpgsqlDbType type, object? value)
    {
        _parameters.Add(name, new TypedParameter(type, value));

        return this;
    }
}

internal sealed class TypedParameter(NpgsqlDbType type, object? value) : SqlMapper.ICustomQueryParameter
{
    public void AddParameter(IDbCommand command, string name)
    {
        var parameter = new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value };

        command.Parameters.Add(parameter);
    }
}
