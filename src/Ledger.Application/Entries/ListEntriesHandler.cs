using Ledger.Application.Abstractions;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;

namespace Ledger.Application.Entries;

public sealed class ListEntriesHandler(
    IStatementReader reader,
    IStatementCursorProtector cursorProtector,
    IReadTelemetry telemetry,
    ReadAudit audit)
{
    public async Task<Result<StatementPage>> HandleAsync(ListEntriesQuery query, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Limit, 1);

        using var operation = telemetry.BeginStatement(query.Limit);

        var from = query.From?.ToUniversalTime();
        var to = query.To?.ToUniversalTime();
        var bounds = StatementBounds.Resolve(from, to, query.Cursor);
        var rows = await reader.ReadPageAsync(query.AccountId, bounds, query.Limit + 1, cancellationToken);

        if (rows.Count == 0 && query.Cursor is null
                            && !await reader.AccountExistsAsync(query.AccountId, cancellationToken))
        {
            return AccountErrors.NotFound;
        }

        var hasNext = rows.Count > query.Limit;
        var items = hasNext ? rows.Take(query.Limit).ToList() : rows;
        var nextCursor = hasNext ? ProtectCursor(query.AccountId, items[^1]) : null;

        operation.Returned(items.Count, hasNext);

        audit.StatementQueried(
            query.ClientId,
            query.AccountId,
            query.CorrelationId,
            from,
            to,
            query.Limit,
            items.Count,
            query.Cursor is not null);

        return new StatementPage(items, nextCursor, query.Limit);
    }

    private string ProtectCursor(AccountId accountId, EntryView last) =>
        cursorProtector.Protect(accountId, new StatementPosition(last.RecordedAt, last.AccountVersion));
}
