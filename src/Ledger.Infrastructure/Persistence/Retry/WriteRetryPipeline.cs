using Ledger.Infrastructure.Observability;
using Ledger.Infrastructure.Observability.Labels;
using Ledger.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Polly;
using Polly.Retry;

namespace Ledger.Infrastructure.Persistence.Retry;

internal sealed class WriteRetryPipeline
{
    private readonly ResiliencePipeline _pipeline;

    public WriteRetryPipeline(
        IOptions<ResilienceOptions> options,
        TimeProvider timeProvider,
        DbTelemetry telemetry,
        ILogger<WriteRetryPipeline> logger)
    {
        var retry = options.Value.Retry;
        var builder = new ResiliencePipelineBuilder { TimeProvider = timeProvider };

        if (retry.MaxRetryAttempts > 0)
        {
            builder.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = retry.MaxRetryAttempts,
                Delay = TimeSpan.FromMilliseconds(retry.BaseDelayMs),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = arguments => ValueTask.FromResult(
                    arguments.Outcome.Exception is { } exception && PostgresRetryPredicate.ShouldRetry(exception)),
                OnRetry = arguments =>
                {
                    var sqlState = (arguments.Outcome.Exception as PostgresException)?.SqlState;

                    PersistenceLog.TransientDatabaseFailure(
                        logger,
                        sqlState ?? PersistenceLog.NoSqlState,
                        arguments.AttemptNumber + 1);
                    telemetry.TransactionRetried(ReasonOf(sqlState));

                    return ValueTask.CompletedTask;
                }
            });
        }

        _pipeline = builder.Build();
    }

    public ValueTask<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, ValueTask<TResult>> callback,
        CancellationToken cancellationToken) =>
        _pipeline.ExecuteAsync(callback, cancellationToken);

    private static DbRetryReason ReasonOf(string? sqlState)
    {
        return sqlState switch
        {
            PostgresErrorCodes.DeadlockDetected => DbRetryReason.Deadlock,
            PostgresErrorCodes.SerializationFailure => DbRetryReason.SerializationFailure,
            _ => DbRetryReason.TransientConnection
        };
    }
}
