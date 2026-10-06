using System.Globalization;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;

namespace Ledger.Application.Integrity;

public static class IntegrityClassifier
{
    private const string MissingText = "missing";
    private const string NoneText = "none";
    private const string InstantFormat = "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'";

    public static IReadOnlyList<IntegrityFinding> FromHead(HeadRow row)
    {
        var findings = new List<IntegrityFinding>(4);

        var expectedBalance = row.LatestBalanceAfter ?? decimal.Zero;

        if (row.Balance != expectedBalance)
        {
            findings.Add(HeadFinding(IntegrityCheck.HeadBalance, row, Money(expectedBalance), Money(row.Balance)));
        }

        var expectedVersion = row.LatestAccountVersion ?? 0L;

        if (row.Version != expectedVersion)
        {
            findings.Add(HeadFinding(IntegrityCheck.HeadVersion, row, Number(expectedVersion), Number(row.Version)));
        }

        if (row.LastEntryId != row.LatestEntryId)
        {
            findings.Add(HeadFinding(
                IntegrityCheck.HeadLastEntry,
                row,
                EntryText(row.LatestEntryId),
                EntryText(row.LastEntryId)));
        }

        if (row.Balance < -row.OverdraftLimit)
        {
            findings.Add(HeadFinding(IntegrityCheck.HeadFloor, row, Money(-row.OverdraftLimit), Money(row.Balance)));
        }

        return findings;
    }

    public static IReadOnlyList<IntegrityFinding> FromChain(ChainRow row)
    {
        var findings = new List<IntegrityFinding>(2);

        if (row.MissingPredecessor)
        {
            findings.Add(ChainFinding(IntegrityCheck.ChainGap, row, Number(row.AccountVersion - 1), MissingText));
        }
        else if (row.BalanceDrift != decimal.Zero)
        {
            findings.Add(ChainFinding(
                IntegrityCheck.ChainDrift,
                row,
                Money(row.BalanceAfter - row.BalanceDrift),
                Money(row.BalanceAfter)));
        }

        if (row.NonMonotonic)
        {
            findings.Add(ChainFinding(
                IntegrityCheck.ChainNonMonotonic,
                row,
                "after " + PreviousInstant(row.PreviousRecordedAt),
                Instant(row.RecordedAt)));
        }

        return findings;
    }

    public static IReadOnlyList<IntegrityFinding> FromSum(AccountId accountId, decimal storedBalance, decimal entriesSum)
    {
        if (storedBalance == entriesSum)
        {
            return [];
        }

        return
        [
            new IntegrityFinding(
                IntegrityCheck.SumBalance,
                accountId,
                null,
                null,
                Money(entriesSum),
                Money(storedBalance))
        ];
    }

    private static IntegrityFinding HeadFinding(IntegrityCheck check, HeadRow row, string expected, string found) =>
        new(check, row.AccountId, null, null, expected, found);

    private static IntegrityFinding ChainFinding(IntegrityCheck check, ChainRow row, string expected, string found) =>
        new(check, row.AccountId, row.EntryId, row.AccountVersion, expected, found);

    private static string Money(decimal amount) =>
        (amount == decimal.Zero ? decimal.Zero : amount).ToString("F2", CultureInfo.InvariantCulture);

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string EntryText(EntryId? entryId) => entryId?.ToString() ?? NoneText;

    private static string Instant(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString(InstantFormat, CultureInfo.InvariantCulture);

    private static string PreviousInstant(DateTimeOffset? instant) =>
        instant is { } value ? Instant(value) : "predecessor";
}
