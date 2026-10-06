using Ledger.Domain.Accounts;
using Microsoft.Extensions.Logging;

namespace Ledger.Application;

public sealed partial class ReadAudit
{
    public const string CategoryName = "Ledger.Audit";

    private readonly ILogger _logger;

    public ReadAudit(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger(CategoryName);
    }

    [LoggerMessage(
        EventId = 2001,
        EventName = "BalanceQueried",
        Level = LogLevel.Information,
        Message =
            "Balance queried by client {ClientId} for account {AccountId} (correlation {CorrelationId}, mode {Mode}, as of {AsOf:O})")]
    public partial void BalanceQueried(
        string clientId,
        AccountId accountId,
        string correlationId,
        string mode,
        DateTimeOffset? asOf);

    [LoggerMessage(
        EventId = 2002,
        EventName = "StatementQueried",
        Level = LogLevel.Information,
        Message =
            "Statement queried by client {ClientId} for account {AccountId} (correlation {CorrelationId}, from {From:O}, to {To:O}, limit {Limit}, returned {Returned}, cursor {HasCursor})")]
    public partial void StatementQueried(
        string clientId,
        AccountId accountId,
        string correlationId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int limit,
        int returned,
        bool hasCursor);
}
