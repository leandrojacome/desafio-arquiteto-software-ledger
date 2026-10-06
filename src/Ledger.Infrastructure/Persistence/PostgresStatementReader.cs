using Dapper;
using Ledger.Application.Abstractions;
using Ledger.Application.Entries;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Infrastructure.Observability;
using Ledger.Infrastructure.Observability.Labels;

namespace Ledger.Infrastructure.Persistence;

internal sealed class PostgresStatementReader(
    IPostgresConnectionFactory connectionFactory,
    DbTelemetry telemetry,
    TimeProvider timeProvider) : IStatementReader
{
    internal const string ReadStatementPageSql = """
                                                SELECT e.id, e.account_version, e.type, e.amount, e.currency, e.balance_after,
                                                       e.recorded_at, e.occurred_at, e.description, e.reference, e.reverses_entry_id
                                                FROM ledger_entries AS e
                                                WHERE e.account_id = @account_id
                                                  AND e.recorded_at >= COALESCE(@from, '-infinity'::timestamptz)
                                                  AND e.recorded_at <= COALESCE(@upper_recorded_at, 'infinity'::timestamptz)
                                                  AND (e.recorded_at, e.account_version) < (
                                                        COALESCE(@upper_recorded_at, 'infinity'::timestamptz),
                                                        COALESCE(@upper_account_version, 9223372036854775807)
                                                      )
                                                ORDER BY e.recorded_at DESC, e.account_version DESC
                                                LIMIT @limit_plus_one;
                                                """;

    private const string AccountExistsSql = """
                                            SELECT EXISTS (SELECT 1 FROM accounts AS a WHERE a.id = @account_id);
                                            """;

    public async Task<IReadOnlyList<EntryView>> ReadPageAsync(
        AccountId accountId,
        StatementBounds bounds,
        int limitPlusOne,
        CancellationToken cancellationToken)
    {
        var parameters = new SqlParameters()
            .Uuid("account_id", accountId.Value)
            .TimestampTz("from", bounds.From)
            .TimestampTz("upper_recorded_at", bounds.Upper?.RecordedAt)
            .Bigint("upper_account_version", bounds.Upper?.AccountVersion)
            .Integer("limit_plus_one", limitPlusOne)
            .Build();
        var command = new CommandDefinition(ReadStatementPageSql, parameters, cancellationToken: cancellationToken);

        await using var connection = await connectionFactory.OpenConnectionAsync(
            PostgresSource.Statement,
            cancellationToken);
        var started = timeProvider.GetTimestamp();
        await using var reader = await connection.ExecuteReaderAsync(command);

        var rows = new List<EntryView>();

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(await RowMapping.ReadStatementEntryAsync(reader, accountId, cancellationToken));
        }

        telemetry.CommandCompleted(DbOperation.SelectEntries, timeProvider.GetElapsedTime(started));

        return rows;
    }

    public async Task<bool> AccountExistsAsync(AccountId accountId, CancellationToken cancellationToken)
    {
        var parameters = new SqlParameters().Uuid("account_id", accountId.Value).Build();
        var command = new CommandDefinition(AccountExistsSql, parameters, cancellationToken: cancellationToken);

        await using var connection = await connectionFactory.OpenConnectionAsync(
            PostgresSource.Statement,
            cancellationToken);

        return await connection.ExecuteScalarAsync<bool>(command);
    }
}
