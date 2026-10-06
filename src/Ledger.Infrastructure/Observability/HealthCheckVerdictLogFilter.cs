using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Ledger.Infrastructure.Observability;

internal static class HealthCheckVerdictLogFilter
{
    private const string FrameworkCategory = "Microsoft.Extensions.Diagnostics.HealthChecks.DefaultHealthCheckService";
    private const string EventIdProperty = "EventId";
    private const string IdField = "Id";
    private const string CheckNameProperty = "HealthCheckName";
    private const int VerdictEventId = 103;

    private static readonly HashSet<string> ChecksWithTheirOwnEvent = new(StringComparer.Ordinal)
    {
        "postgres",
        "schema",
        "rabbitmq",
        "broker-circuit",
        "shutdown"
    };

    public static LoggerConfiguration WithoutDuplicatedHealthCheckVerdicts(this LoggerConfiguration configuration)
    {
        return configuration.Filter.ByExcluding(IsDuplicatedVerdict);
    }

    private static bool IsDuplicatedVerdict(LogEvent logEvent)
    {
        return IsFromFramework(logEvent)
               && IsVerdict(logEvent)
               && logEvent.Properties.TryGetValue(CheckNameProperty, out var name)
               && name is ScalarValue { Value: string checkName }
               && ChecksWithTheirOwnEvent.Contains(checkName);
    }

    private static bool IsFromFramework(LogEvent logEvent)
    {
        return logEvent.Properties.TryGetValue(Constants.SourceContextPropertyName, out var value)
               && value is ScalarValue { Value: string category }
               && string.Equals(category, FrameworkCategory, StringComparison.Ordinal);
    }

    private static bool IsVerdict(LogEvent logEvent)
    {
        return logEvent.Properties.TryGetValue(EventIdProperty, out var value)
               && value is StructureValue structure
               && structure.Properties.Any(property =>
                   property.Name == IdField && property.Value is ScalarValue { Value: int id } && id == VerdictEventId);
    }
}
