namespace Ledger.Worker;

internal static partial class WorkerLog
{
    [LoggerMessage(
        EventId = 3008,
        EventName = "WorkerLoopFailed",
        Level = LogLevel.Warning,
        Message = "Worker loop {Loop} failed ({ExceptionType}), next attempt in {NextDelaySeconds} seconds")]
    public static partial void WorkerLoopFailed(
        ILogger logger,
        string loop,
        string exceptionType,
        double nextDelaySeconds);

    [LoggerMessage(
        EventId = 4003,
        EventName = "IntegrityRunFailed",
        Level = LogLevel.Warning,
        Message = "Integrity run ({Mode}) was interrupted ({ExceptionType}) and will be retried by the next run")]
    public static partial void IntegrityRunFailed(ILogger logger, string mode, string exceptionType);

    [LoggerMessage(
        EventId = 5021,
        EventName = "WorkerConfigurationInvalid",
        Level = LogLevel.Critical,
        Message = "Invalid configuration for the worker: {Failures}")]
    public static partial void WorkerConfigurationInvalid(ILogger logger, string failures);
}
