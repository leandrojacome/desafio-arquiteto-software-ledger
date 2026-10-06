namespace Ledger.Api.RateLimiting;

internal static partial class RateLimitLog
{
    [LoggerMessage(
        EventId = 6101,
        EventName = "RateLimitExceeded",
        Level = LogLevel.Information,
        Message = "Rate limit exceeded on policy {Policy} for client {ClientId}, retry after {RetryAfterSeconds} s")]
    public static partial void RateLimitExceeded(ILogger logger, string policy, string? clientId, int retryAfterSeconds);

    [LoggerMessage(
        EventId = 6102,
        EventName = "ConcurrencyLimitExceeded",
        Level = LogLevel.Warning,
        Message = "Concurrency limit reached on policy {Policy} for client {ClientId}")]
    public static partial void ConcurrencyLimitExceeded(ILogger logger, string policy, string? clientId);
}
