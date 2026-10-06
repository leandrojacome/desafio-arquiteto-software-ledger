using Dapper;
using Ledger.Application.Abstractions;
using Ledger.Application.Entries;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;
using Npgsql;

namespace Ledger.Infrastructure.Persistence;

internal sealed class PostgresEntryRepository(NpgsqlConnection connection, NpgsqlTransaction transaction)
    : IEntryRepository
{
    private const string ReversalConstraint = "uq_ledger_entries_reverses_entry_id";
    private const int RecordedAtCorrectedOrdinal = RowMapping.EntryColumns;

    private const string ApplyEntrySql = """
                                         WITH applied AS (
                                             UPDATE account_balances AS ab
                                             SET balance = ab.balance + @delta,
                                                 version = ab.version + 1,
                                                 last_entry_id = @entry_id,
                                                 last_recorded_at = GREATEST(clock_timestamp(), ab.last_recorded_at + INTERVAL '1 microsecond')
                                             FROM accounts AS a
                                             WHERE ab.account_id = @account_id
                                               AND a.id = ab.account_id
                                               AND a.currency = @currency
                                               AND ab.balance + @delta >= -ab.overdraft_limit
                                             RETURNING ab.balance AS balance_after,
                                                       ab.version AS account_version,
                                                       ab.last_recorded_at AS recorded_at,
                                                       ab.last_recorded_at > clock_timestamp() AS recorded_at_corrected
                                         ),
                                         inserted AS (
                                             INSERT INTO ledger_entries (id, account_id, account_version, type, amount, currency, balance_after,
                                                                         recorded_at, occurred_at, description, reference, reverses_entry_id,
                                                                         client_id, correlation_id)
                                             SELECT @entry_id, @account_id, applied.account_version, @type, @amount, @currency, applied.balance_after,
                                                    applied.recorded_at, COALESCE(@occurred_at, applied.recorded_at), @description, @reference,
                                                    @reverses_entry_id, @client_id, @correlation_id
                                             FROM applied
                                             RETURNING id, account_id, account_version, type, amount, currency, balance_after,
                                                       recorded_at, occurred_at, description, reference, reverses_entry_id
                                         )
                                         SELECT inserted.id, inserted.account_id, inserted.account_version, inserted.type, inserted.amount,
                                                inserted.currency, inserted.balance_after, inserted.recorded_at, inserted.occurred_at,
                                                inserted.description, inserted.reference, inserted.reverses_entry_id,
                                                applied.recorded_at_corrected
                                         FROM inserted
                                         CROSS JOIN applied;
                                         """;

    private const string ReadReversalCandidateSql = """
                                                    SELECT o.id, o.type, o.amount, o.currency, o.reverses_entry_id, r.id AS reversal_id
                                                    FROM ledger_entries AS o
                                                    LEFT JOIN ledger_entries AS r ON r.reverses_entry_id = o.id
                                                    WHERE o.id = @entry_id
                                                      AND o.account_id = @account_id;
                                                    """;

    public async Task<Result<AppliedEntry>> TryApplyAsync(NewEntry newEntry, CancellationToken cancellationToken)
    {
        var entry = newEntry.Entry;
        var parameters = new SqlParameters()
            .Uuid("entry_id", entry.Id.Value)
            .Uuid("account_id", entry.AccountId.Value)
            .Numeric("delta", entry.SignedDelta)
            .Char("currency", entry.Amount.Currency)
            .Varchar("type", entry.Type.ToDatabaseText())
            .Numeric("amount", entry.Amount.Amount)
            .TimestampTz("occurred_at", entry.OccurredAt)
            .Varchar("description", entry.Description)
            .Varchar("reference", entry.Reference)
            .Uuid("reverses_entry_id", entry.ReversesEntryId?.Value)
            .Varchar("client_id", newEntry.ClientId)
            .Varchar("correlation_id", newEntry.CorrelationId)
            .Build();

        var command = new CommandDefinition(ApplyEntrySql, parameters, transaction, cancellationToken: cancellationToken);

        try
        {
            await using var reader = await connection.ExecuteReaderAsync(command);

            if (!await reader.ReadAsync(cancellationToken))
            {
                return ApplyErrors.NotMatched;
            }

            var applied = await RowMapping.ReadEntryAsync(reader, 0, cancellationToken);

            return new AppliedEntry(applied, reader.GetBoolean(RecordedAtCorrectedOrdinal));
        }
        catch (PostgresException exception) when (IsReversalRace(exception))
        {
            return EntryErrors.AlreadyReversed;
        }
    }

    public async Task<ReversalCandidate?> FindForReversalAsync(
        AccountId accountId,
        EntryId entryId,
        CancellationToken cancellationToken)
    {
        var parameters = new SqlParameters()
            .Uuid("entry_id", entryId.Value)
            .Uuid("account_id", accountId.Value)
            .Build();

        var command = new CommandDefinition(
            ReadReversalCandidateSql,
            parameters,
            transaction,
            cancellationToken: cancellationToken);

        await using var reader = await connection.ExecuteReaderAsync(command);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ReversalCandidate(
            RowMapping.RequireEntryId(reader.GetGuid(0)),
            RowMapping.RequireType(reader.GetString(1)),
            RowMapping.RequireMoney(reader.GetDecimal(2), reader.GetString(3)),
            await RowMapping.ReadOptionalEntryIdAsync(reader, 4, cancellationToken),
            await RowMapping.ReadOptionalEntryIdAsync(reader, 5, cancellationToken));
    }

    private static bool IsReversalRace(PostgresException exception) =>
        exception.SqlState == PostgresErrorCodes.UniqueViolation && exception.ConstraintName == ReversalConstraint;
}
