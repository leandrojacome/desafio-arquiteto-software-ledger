using Ledger.Domain.Shared;

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

public readonly record struct StatementPosition(DateTimeOffset RecordedAt, long AccountVersion)
    : IComparable<StatementPosition>
{
    public static StatementPosition Before(DateTimeOffset instant) => new(instant, long.MinValue);

    public static bool operator <(StatementPosition left, StatementPosition right) => left.CompareTo(right) < 0;

    public static bool operator >(StatementPosition left, StatementPosition right) => left.CompareTo(right) > 0;

    public static bool operator <=(StatementPosition left, StatementPosition right) => left.CompareTo(right) <= 0;

    public static bool operator >=(StatementPosition left, StatementPosition right) => left.CompareTo(right) >= 0;

    public int CompareTo(StatementPosition other)
    {
        var byInstant = RecordedAt.CompareTo(other.RecordedAt);

        return byInstant != 0 ? byInstant : AccountVersion.CompareTo(other.AccountVersion);
    }
}

public static class StatementErrors
{
    public static readonly Error InvalidCursor =
        new("VALIDATION_FAILED", "O cursor não é válido para esta conta.", ErrorKind.Validation);
}
