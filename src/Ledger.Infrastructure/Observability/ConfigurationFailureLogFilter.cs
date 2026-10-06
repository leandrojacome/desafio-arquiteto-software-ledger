using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Ledger.Infrastructure.Observability;

internal static class ConfigurationFailureLogFilter
{
    private const string HostCategory = "Microsoft.Extensions.Hosting.Internal.Host";

    public static LoggerConfiguration WithoutConfigurationFailureTraces(this LoggerConfiguration configuration)
    {
        return configuration.Filter.ByExcluding(IsConfigurationFailureTrace);
    }

    private static bool IsConfigurationFailureTrace(LogEvent logEvent)
    {
        return IsFromHost(logEvent) && IsConfigurationFailure(logEvent.Exception);
    }

    private static bool IsFromHost(LogEvent logEvent)
    {
        return logEvent.Properties.TryGetValue(Constants.SourceContextPropertyName, out var value)
               && value is ScalarValue { Value: string category }
               && string.Equals(category, HostCategory, StringComparison.Ordinal);
    }

    private static bool IsConfigurationFailure(Exception? exception)
    {
        return exception switch
        {
            OptionsValidationException => true,
            AggregateException aggregate => aggregate.InnerExceptions.Count > 0
                                            && aggregate.InnerExceptions.All(IsConfigurationFailure),
            _ => false
        };
    }
}
