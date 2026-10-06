using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Microsoft.Extensions.Logging;

namespace Ledger.Application.Entries;

internal static partial class EntryLog
{
    [LoggerMessage(
        EventId = 1001,
        EventName = "EntryAccepted",
        Level = LogLevel.Debug,
        Message = "Entry {EntryId} accepted for account {AccountId} from client {ClientId}")]
    public static partial void EntryAccepted(ILogger logger, EntryId entryId, AccountId accountId, string clientId);

    [LoggerMessage(
        EventId = 1002,
        EventName = "IdempotentReplayDelivered",
        Level = LogLevel.Information,
        Message =
            "Idempotent replay delivered for entry {EntryId} on account {AccountId} to client {ClientId} (key {KeyFingerprint})")]
    public static partial void IdempotentReplayDelivered(
        ILogger logger,
        EntryId entryId,
        AccountId accountId,
        string clientId,
        string keyFingerprint);

    [LoggerMessage(
        EventId = 1003,
        EventName = "IdempotencyKeyReused",
        Level = LogLevel.Warning,
        Message =
            "Idempotency key {KeyFingerprint} was reused with a different request on account {AccountId} by client {ClientId} ({Operation})")]
    public static partial void IdempotencyKeyReused(
        ILogger logger,
        string keyFingerprint,
        AccountId accountId,
        string clientId,
        string operation);

    [LoggerMessage(
        EventId = 1004,
        EventName = "InsufficientFundsRejected",
        Level = LogLevel.Information,
        Message =
            "Entry rejected for insufficient funds on account {AccountId} from client {ClientId} (key {KeyFingerprint})")]
    public static partial void InsufficientFundsRejected(
        ILogger logger,
        AccountId accountId,
        string clientId,
        string keyFingerprint);

    [LoggerMessage(
        EventId = 1005,
        EventName = "RecordedAtCorrected",
        Level = LogLevel.Warning,
        Message =
            "Recorded instant of entry {EntryId} on account {AccountId} was moved ahead of the database clock (client {ClientId})")]
    public static partial void RecordedAtCorrected(ILogger logger, EntryId entryId, AccountId accountId, string clientId);
}
