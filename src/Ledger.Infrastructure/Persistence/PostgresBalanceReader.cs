using Dapper;
using Ledger.Application.Abstractions;
using Ledger.Application.Balances;
using Ledger.Domain.Accounts;
using Ledger.Domain.Shared;
using Ledger.Infrastructure.Observability;
using Ledger.Infrastructure.Observability.Labels;

namespace Ledger.Infrastructure.Persistence;

internal sealed class PostgresBalanceReader(
    IPostgresConnectionFactory connectionFactory,
    DbTelemetry telemetry,
    TimeProvider timeProvider) : IBalanceReader
{
    internal const string ReadCurrentBalanceSql = """
                                                 SELECT a.currency, ab.balance, ab.overdraft_limit, ab.last_entry_id, clock_timestamp() AS database_now
                                                 FROM accounts AS a
                                                 JOIN account_balances AS ab ON ab.account_id = a.id
                                                 WHERE a.id = @account_id;
                                                 """;

    internal const string ReadBalanceAtSql = """
                                            SELECT a.currency, ab.overdraft_limit, clock_timestamp() AS database_now,
                                                   last_entry.id AS last_entry_id, last_entry.balance_after, last_entry.recorded_at AS last_recorded_at
                                            FROM accounts AS a
                                            JOIN account_balances AS ab ON ab.account_id = a.id
                                            LEFT JOIN LATERAL (
                                                SELECT e.id, e.balance_after, e.recorded_at
                                                FROM ledger_entries AS e
                                                WHERE e.account_id = a.id
                                                  AND e.recorded_at <= @as_of
                                                ORDER BY e.recorded_at DESC, e.account_version DESC
                                                LIMIT 1
                                            ) AS last_entry ON TRUE
                                            WHERE a.id = @account_id;
                                            """;

    public async Task<Result<CurrentBalanceReading>> ReadCurrentAsync(
        AccountId accountId,
        CancellationToken cancellationToken)
    {
        var parameters = new SqlParameters().Uuid("account_id", accountId.Value).Build();
        var command = new CommandDefinition(ReadCurrentBalanceSql, parameters, cancellationToken: cancellationToken);

        await using var connection = await connectionFactory.OpenConnectionAsync(PostgresSource.Balance, cancellationToken);
        var started = timeProvider.GetTimestamp();
        await using var reader = await connection.ExecuteReaderAsync(command);

        if (!await reader.ReadAsync(cancellationToken))
        {
            telemetry.CommandCompleted(DbOperation.SelectBalance, timeProvider.GetElapsedTime(started));

            return AccountErrors.NotFound;
        }

        telemetry.CommandCompleted(DbOperation.SelectBalance, timeProvider.GetElapsedTime(started));

        return new CurrentBalanceReading(
            reader.GetString(0),
            reader.GetDecimal(1),
            reader.GetDecimal(2),
            await RowMapping.ReadOptionalEntryIdAsync(reader, 3, cancellationToken),
            await reader.GetFieldValueAsync<DateTimeOffset>(4, cancellationToken));
    }

    public async Task<Result<BalanceAtReading>> ReadAtAsync(
        AccountId accountId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        var parameters = new SqlParameters()
            .Uuid("account_id", accountId.Value)
            .TimestampTz("as_of", asOf)
            .Build();
        var command = new CommandDefinition(ReadBalanceAtSql, parameters, cancellationToken: cancellationToken);

        await using var connection = await connectionFactory.OpenConnectionAsync(PostgresSource.Balance, cancellationToken);
        var started = timeProvider.GetTimestamp();
        await using var reader = await connection.ExecuteReaderAsync(command);

        if (!await reader.ReadAsync(cancellationToken))
        {
            telemetry.CommandCompleted(DbOperation.SelectBalanceAsOf, timeProvider.GetElapsedTime(started));

            return AccountErrors.NotFound;
        }

        telemetry.CommandCompleted(DbOperation.SelectBalanceAsOf, timeProvider.GetElapsedTime(started));

        return new BalanceAtReading(
            reader.GetString(0),
            reader.GetDecimal(1),
            await reader.IsDBNullAsync(4, cancellationToken) ? null : reader.GetDecimal(4),
            await RowMapping.ReadOptionalEntryIdAsync(reader, 3, cancellationToken),
            await reader.GetFieldValueAsync<DateTimeOffset>(2, cancellationToken));
    }
}
