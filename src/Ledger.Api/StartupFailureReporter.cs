using System.Reflection;
using Ledger.Infrastructure.Observability;
using Microsoft.Extensions.Options;
using Serilog.Extensions.Logging;

namespace Ledger.Api;

internal static class StartupFailureReporter
{
    public const int InvalidConfigurationExitCode = 3;

    private const string FailureSeparator = "; ";

    public static bool OwnsTheProcess { get; } =
        ReferenceEquals(Assembly.GetEntryAssembly(), typeof(StartupFailureReporter).Assembly);

    public static IReadOnlyList<string>? FailuresOf(Exception exception)
    {
        var failures = new List<string>();

        return TryCollect(exception, failures) ? failures : null;
    }

    public static int ReportInvalidConfiguration(IEnumerable<string> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);

        using var startupLogger = StartupLogger.Create(ApiConstants.ServiceName);
        using var loggerFactory = new SerilogLoggerFactory(startupLogger);

        var logger = loggerFactory.CreateLogger(ApiConstants.ServiceName);

        if (logger.IsEnabled(LogLevel.Critical))
        {
            var joined = string.Join(FailureSeparator, failures);

            StartupLog.ApiConfigurationInvalid(logger, joined);
        }

        return InvalidConfigurationExitCode;
    }

    private static bool TryCollect(Exception exception, List<string> failures)
    {
        switch (exception)
        {
            case OptionsValidationException validation:
                failures.AddRange(validation.Failures);

                return true;
            case AggregateException aggregate:
                return aggregate.InnerExceptions.All(inner => TryCollect(inner, failures));
            default:
                return false;
        }
    }
}
