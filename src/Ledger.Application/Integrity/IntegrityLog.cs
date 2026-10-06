using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Microsoft.Extensions.Logging;

namespace Ledger.Application.Integrity;

internal static partial class IntegrityLog
{
    [LoggerMessage(
        EventId = 4001,
        EventName = "IntegrityRunCompleted",
        Level = LogLevel.Information,
        Message =
            "Integrity run {RunId} ({Mode}) completed: {AccountsChecked} accounts, {EntriesChecked} entries, {Violations} violations in {Duration}")]
    public static partial void IntegrityRunCompleted(
        ILogger logger,
        string mode,
        string runId,
        long accountsChecked,
        long entriesChecked,
        int violations,
        TimeSpan duration);

    [LoggerMessage(
        EventId = 4005,
        EventName = "IntegrityWindowTruncated",
        Level = LogLevel.Warning,
        Message =
            "The recent integrity window started {Behind} behind and was cut to the full lookback of {Lookback}; the older part is left to the full run")]
    public static partial void RecentWindowTruncated(ILogger logger, TimeSpan behind, TimeSpan lookback);

    [LoggerMessage(
        EventId = 4002,
        EventName = "IntegrityViolationDetected",
        Level = LogLevel.Error,
        Message =
            "Integrity run {RunId} ({Mode}) found {Check} on account {AccountId} (entry {EntryId}, version {AccountVersion})")]
    public static partial void IntegrityViolationDetected(
        ILogger logger,
        string mode,
        string runId,
        string check,
        AccountId accountId,
        EntryId? entryId,
        long? accountVersion);
}
