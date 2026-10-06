namespace Ledger.Domain.Entries;

public sealed record StatementBounds
{
    private StatementBounds(DateTimeOffset? from, StatementPosition? upper)
    {
        From = from;
        Upper = upper;
    }

    public DateTimeOffset? From { get; }

    public StatementPosition? Upper { get; }

    public static StatementBounds Resolve(DateTimeOffset? from, DateTimeOffset? to, StatementPosition? cursor)
    {
        var utcFrom = from?.ToUniversalTime();

        if (to is null)
        {
            return new StatementBounds(utcFrom, cursor);
        }

        var beforeTo = StatementPosition.Before(to.Value.ToUniversalTime());

        return cursor is { } position && position < beforeTo
            ? new StatementBounds(utcFrom, position)
            : new StatementBounds(utcFrom, beforeTo);
    }
}
