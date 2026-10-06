using Ledger.Domain.Accounts;
using Microsoft.Extensions.Logging;

namespace Ledger.Application.Accounts;

internal static partial class AccountLog
{
    [LoggerMessage(
        EventId = 7001,
        EventName = "AccountCreated",
        Level = LogLevel.Information,
        Message = "Account {AccountId} created by client {ClientId}")]
    public static partial void AccountCreated(ILogger logger, AccountId accountId, string clientId);

    [LoggerMessage(
        EventId = 7002,
        EventName = "KeyProviderUnavailable",
        Level = LogLevel.Warning,
        Message = "The key provider is unavailable for {Operation}")]
    public static partial void KeyProviderUnavailable(ILogger logger, string operation);

    [LoggerMessage(
        EventId = 7006,
        EventName = "RewrapBatchCompleted",
        Level = LogLevel.Information,
        Message = "Rewrap batch completed: {Rewrapped} rewrapped, {Failed} failed, target key version {ToVersion}")]
    public static partial void RewrapBatchCompleted(ILogger logger, int rewrapped, int failed, int toVersion);

    [LoggerMessage(
        EventId = 7010,
        EventName = "KeyVersionAheadOfActive",
        Level = LogLevel.Error,
        Message = "Accounts are sealed with key versions {Versions}, newer than the active version {ActiveVersion} of this process; another instance is ahead or this one is stale")]
    public static partial void KeyVersionAheadOfActive(ILogger logger, string versions, int activeVersion);

    [LoggerMessage(
        EventId = 7011,
        EventName = "KeyVersionNotLive",
        Level = LogLevel.Error,
        Message = "Accounts are sealed with key versions {Versions} that this process cannot read")]
    public static partial void KeyVersionNotLive(ILogger logger, string versions);

    [LoggerMessage(
        EventId = 7007,
        EventName = "DocumentDecryptFailed",
        Level = LogLevel.Error,
        Message = "The holder document of account {AccountId} could not be decrypted")]
    public static partial void DocumentDecryptFailed(ILogger logger, AccountId accountId);
}
