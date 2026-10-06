using System.Diagnostics.CodeAnalysis;
using Dapper;
using Ledger.Application.Audit;
using Ledger.Application.Integrity;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace Ledger.Infrastructure.Persistence.Integrity;

internal sealed partial class PostgresIntegritySession : IIntegritySession
{
    private static readonly TimeSpan UnlockTimeout = TimeSpan.FromSeconds(5);

    private const string UnlockSql = "SELECT pg_advisory_unlock(@namespace, @mode_key);";

    private const string DatabaseNowSql = "SELECT clock_timestamp();";

    internal const string CheckHeadsSql = """
        SELECT b.account_id, b.balance, b.overdraft_limit, b.version, b.last_entry_id,
               last_entry.balance_after, last_entry.account_version, last_entry.id AS entry_id
        FROM account_balances AS b
        LEFT JOIN LATERAL (
            SELECT e.id, e.account_version, e.balance_after
            FROM ledger_entries AS e
            WHERE e.account_id = b.account_id
            ORDER BY e.recorded_at DESC, e.account_version DESC
            LIMIT 1
        ) AS last_entry ON TRUE
        WHERE b.account_id = ANY(@account_ids)
          AND (   b.balance <> COALESCE(last_entry.balance_after, 0)
               OR b.version <> COALESCE(last_entry.account_version, 0)
               OR b.last_entry_id IS DISTINCT FROM last_entry.id
               OR b.balance < -b.overdraft_limit);
        """;

    internal const string CheckChainSql = """
        SELECT e.account_id, e.account_version, e.id, e.balance_after, e.recorded_at,
               p.recorded_at AS previous_recorded_at,
               e.balance_after - COALESCE(p.balance_after, 0)
                 - CASE e.type WHEN 'CREDIT' THEN e.amount ELSE -e.amount END AS balance_drift,
               (p.id IS NULL AND e.account_version > 1) AS missing_predecessor,
               (p.id IS NOT NULL AND e.recorded_at <= p.recorded_at) AS non_monotonic
        FROM ledger_entries AS e
        LEFT JOIN LATERAL (
            SELECT q.id, q.balance_after, q.recorded_at
            FROM ledger_entries AS q
            WHERE q.account_id = e.account_id AND q.account_version = e.account_version - 1
            LIMIT 1
        ) AS p ON TRUE
        WHERE e.recorded_at >= @window_start
          AND e.recorded_at < @window_end
          AND (   e.balance_after - COALESCE(p.balance_after, 0)
                    - CASE e.type WHEN 'CREDIT' THEN e.amount ELSE -e.amount END <> 0
               OR (p.id IS NULL AND e.account_version > 1)
               OR (p.id IS NOT NULL AND e.recorded_at <= p.recorded_at));
        """;

    private const string CheckAccountChainSql = """
        SELECT e.account_id, e.account_version, e.id, e.balance_after, e.recorded_at,
               p.recorded_at AS previous_recorded_at,
               e.balance_after - COALESCE(p.balance_after, 0)
                 - CASE e.type WHEN 'CREDIT' THEN e.amount ELSE -e.amount END AS balance_drift,
               (p.id IS NULL AND e.account_version > 1) AS missing_predecessor,
               (p.id IS NOT NULL AND e.recorded_at <= p.recorded_at) AS non_monotonic
        FROM ledger_entries AS e
        LEFT JOIN LATERAL (
            SELECT q.id, q.balance_after, q.recorded_at
            FROM ledger_entries AS q
            WHERE q.account_id = e.account_id AND q.account_version = e.account_version - 1
            LIMIT 1
        ) AS p ON TRUE
        WHERE e.account_id = @account_id
          AND (   e.balance_after - COALESCE(p.balance_after, 0)
                    - CASE e.type WHEN 'CREDIT' THEN e.amount ELSE -e.amount END <> 0
               OR (p.id IS NULL AND e.account_version > 1)
               OR (p.id IS NOT NULL AND e.recorded_at <= p.recorded_at));
        """;

    private const string SumCheckSql = """
        SELECT b.balance AS stored_balance,
               COALESCE(s.entries_sum, 0) AS entries_sum,
               COALESCE(s.entry_count, 0) AS entry_count,
               b.version
        FROM account_balances AS b
        LEFT JOIN LATERAL (
            SELECT SUM(CASE e.type WHEN 'CREDIT' THEN e.amount ELSE -e.amount END) AS entries_sum,
                   COUNT(*) AS entry_count
            FROM ledger_entries AS e
            WHERE e.account_id = b.account_id
        ) AS s ON TRUE
        WHERE b.account_id = @account_id;
        """;

    private const string ReadRecentAccountsSql = """
        SELECT DISTINCT e.account_id
        FROM ledger_entries AS e
        WHERE e.recorded_at >= @window_start
          AND e.recorded_at < @window_end
        ORDER BY e.account_id;
        """;

    private const string ReadAccountBatchSql = """
        SELECT b.account_id
        FROM account_balances AS b
        WHERE b.account_id > @after
        ORDER BY b.account_id
        LIMIT @batch_size;
        """;

    private const string CountEntriesSql = """
        SELECT COUNT(*)
        FROM ledger_entries AS e
        WHERE e.recorded_at >= @window_start
          AND e.recorded_at < @window_end;
        """;

    private const string FindLastRunSql = """
        SELECT a.recorded_at, a.outcome, a.details
        FROM audit_log AS a
        WHERE a.event_type = 'integrity.run_completed'
          AND a.details ->> 'mode' = @mode
        ORDER BY a.recorded_at DESC
        LIMIT 1;
        """;

    private readonly NpgsqlConnection _connection;
    private readonly int? _lockKey;
    private readonly int _lockNamespace;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private bool _disposed;

    public PostgresIntegritySession(
        NpgsqlConnection connection,
        int? lockKey,
        int lockNamespace,
        TimeProvider timeProvider,
        ILogger logger)
    {
        _connection = connection;
        _lockKey = lockKey;
        _lockNamespace = lockNamespace;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<DateTimeOffset> GetDatabaseNowAsync(CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(DatabaseNowSql, _connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        await reader.ReadAsync(cancellationToken);

        return await reader.GetFieldValueAsync<DateTimeOffset>(0, cancellationToken);
    }

    public async Task<IntegrityRunRecord?> FindLastRunAsync(IntegrityMode mode, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(FindLastRunSql, _connection);

        command.Parameters.Add("mode", NpgsqlDbType.Text).Value = mode.AuditText();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return IntegrityAuditDetails.ParseRun(
            await reader.GetFieldValueAsync<DateTimeOffset>(0, cancellationToken),
            reader.GetString(1),
            reader.GetString(2));
    }

    public async Task<IReadOnlyList<AccountId>> ReadRecentAccountsAsync(
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(ReadRecentAccountsSql, _connection);

        AddWindow(command, start, end);

        return await ReadAccountIdsAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<AccountId>> ReadAccountBatchAsync(
        AccountId? after,
        int size,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(ReadAccountBatchSql, _connection);

        command.Parameters.Add("after", NpgsqlDbType.Uuid).Value = after?.Value ?? Guid.Empty;
        command.Parameters.Add("batch_size", NpgsqlDbType.Integer).Value = size;

        return await ReadAccountIdsAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<HeadRow>> CheckHeadsAsync(
        IReadOnlyList<AccountId> accounts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        await using var command = new NpgsqlCommand(CheckHeadsSql, _connection);

        command.Parameters.Add("account_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value =
            accounts.Select(account => account.Value).ToArray();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var rows = new List<HeadRow>();

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(MapHead(reader));
        }

        return rows;
    }

    public async Task<IReadOnlyList<ChainRow>> CheckChainAsync(
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(CheckChainSql, _connection);

        AddWindow(command, start, end);

        return await ReadChainAsync(command, cancellationToken);
    }

    public async Task<long> CountEntriesAsync(
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(CountEntriesSql, _connection);

        AddWindow(command, start, end);

        return await command.ExecuteScalarAsync(cancellationToken) is long count ? count : 0L;
    }

    public async Task<AccountIntegrityReport> InspectAccountAsync(
        AccountId accountId,
        CancellationToken cancellationToken)
    {
        var findings = new List<IntegrityFinding>();

        foreach (var row in await CheckHeadsAsync([accountId], cancellationToken))
        {
            findings.AddRange(IntegrityClassifier.FromHead(row));
        }

        foreach (var row in await CheckAccountChainAsync(accountId, cancellationToken))
        {
            findings.AddRange(IntegrityClassifier.FromChain(row));
        }

        var sum = await ReadSumAsync(accountId, cancellationToken);

        findings.AddRange(IntegrityClassifier.FromSum(accountId, sum.StoredBalance, sum.EntriesSum));

        return new AccountIntegrityReport(accountId, sum.StoredBalance, sum.EntriesSum, sum.EntryCount, findings);
    }

    public async Task RecordViolationAsync(
        string runId,
        IntegrityMode mode,
        IntegrityFinding finding,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(finding);

        var auditEvent = AuditEvents.IntegrityViolationDetected(
            runId,
            finding.AccountId,
            IntegrityAuditDetails.ForViolation(runId, mode, finding));

        await _connection.ExecuteAsync(AuditCommands.Insert(auditEvent, null, cancellationToken));
    }

    public async Task RecordRunAsync(string runId, IntegrityRunSummary summary, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var auditEvent = AuditEvents.IntegrityRunCompleted(
            runId,
            summary.Violations == 0,
            IntegrityAuditDetails.ForRun(summary));

        await _connection.ExecuteAsync(AuditCommands.Insert(auditEvent, null, cancellationToken));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            await ReleaseLockAsync();
        }
        finally
        {
            await _connection.DisposeAsync();
        }
    }

    private static void AddWindow(NpgsqlCommand command, DateTimeOffset start, DateTimeOffset end)
    {
        command.Parameters.Add("window_start", NpgsqlDbType.TimestampTz).Value = start;
        command.Parameters.Add("window_end", NpgsqlDbType.TimestampTz).Value = end;
    }

    private static async Task<IReadOnlyList<AccountId>> ReadAccountIdsAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var accounts = new List<AccountId>();

        while (await reader.ReadAsync(cancellationToken))
        {
            accounts.Add(AccountId.From(reader.GetGuid(0)).Value);
        }

        return accounts;
    }

    private static async Task<IReadOnlyList<ChainRow>> ReadChainAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var rows = new List<ChainRow>();

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(MapChain(reader));
        }

        return rows;
    }

    private static HeadRow MapHead(NpgsqlDataReader reader)
    {
        return new HeadRow(
            AccountId.From(reader.GetGuid(0)).Value,
            reader.GetDecimal(1),
            reader.GetDecimal(2),
            reader.GetInt64(3),
            reader.IsDBNull(4) ? null : EntryId.From(reader.GetGuid(4)).Value,
            reader.IsDBNull(5) ? null : reader.GetDecimal(5),
            reader.IsDBNull(6) ? null : reader.GetInt64(6),
            reader.IsDBNull(7) ? null : EntryId.From(reader.GetGuid(7)).Value);
    }

    private static ChainRow MapChain(NpgsqlDataReader reader)
    {
        return new ChainRow(
            AccountId.From(reader.GetGuid(0)).Value,
            reader.GetInt64(1),
            EntryId.From(reader.GetGuid(2)).Value,
            reader.GetDecimal(3),
            reader.GetFieldValue<DateTimeOffset>(4),
            reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
            reader.GetDecimal(6),
            reader.GetBoolean(7),
            reader.GetBoolean(8));
    }

    private async Task<IReadOnlyList<ChainRow>> CheckAccountChainAsync(
        AccountId accountId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(CheckAccountChainSql, _connection);

        command.Parameters.Add("account_id", NpgsqlDbType.Uuid).Value = accountId.Value;

        return await ReadChainAsync(command, cancellationToken);
    }

    private async Task<SumRow> ReadSumAsync(AccountId accountId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(SumCheckSql, _connection);

        command.Parameters.Add("account_id", NpgsqlDbType.Uuid).Value = accountId.Value;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return new SumRow(decimal.Zero, decimal.Zero, 0L);
        }

        return new SumRow(reader.GetDecimal(0), reader.GetDecimal(1), reader.GetInt64(2));
    }

    [SuppressMessage("Design", "CA1031",
        Justification = "Releasing the lock is best effort: when it fails the physical connection is discarded, which frees the lock on the server.")]
    private async Task ReleaseLockAsync()
    {
        if (_lockKey is not { } lockKey)
        {
            return;
        }

        if (_connection.State != System.Data.ConnectionState.Open)
        {
            NpgsqlConnection.ClearPool(_connection);

            return;
        }

        using var deadline = new CancellationTokenSource(UnlockTimeout, _timeProvider);

        try
        {
            await using var command = new NpgsqlCommand(UnlockSql, _connection);

            command.Parameters.Add("namespace", NpgsqlDbType.Integer).Value = _lockNamespace;
            command.Parameters.Add("mode_key", NpgsqlDbType.Integer).Value = lockKey;

            if (await command.ExecuteScalarAsync(deadline.Token) is true)
            {
                return;
            }

            LogLockReleaseFailed(_logger, lockKey, "not_held");
        }
        catch (Exception exception)
        {
            LogLockReleaseFailed(_logger, lockKey, exception.GetType().Name);
        }

        NpgsqlConnection.ClearPool(_connection);
    }

    [LoggerMessage(
        EventId = 4004,
        EventName = "IntegrityLockReleaseFailed",
        Level = LogLevel.Warning,
        Message = "The integrity lock for mode key {ModeKey} could not be released ({Reason}); the connection was discarded")]
    private static partial void LogLockReleaseFailed(ILogger logger, int modeKey, string reason);

    private readonly record struct SumRow(decimal StoredBalance, decimal EntriesSum, long EntryCount);
}
