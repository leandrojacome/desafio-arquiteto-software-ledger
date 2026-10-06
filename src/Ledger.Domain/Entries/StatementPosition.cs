namespace Ledger.Domain.Entries;

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
