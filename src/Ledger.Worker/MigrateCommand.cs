using Ledger.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Ledger.Worker;

internal static partial class MigrateCommand
{
    private const int Succeeded = 0;
    private const int ScriptFailed = 1;
    private const int ConnectionFailed = 2;
    private const int LockTimedOut = 2;
    private const int InvalidConfiguration = 3;

    public static async Task<int> RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        try
        {
            var status = await services.GetRequiredService<IDatabaseMigrator>().MigrateAsync(cancellationToken);

            return ExitCodeFor(status);
        }
        catch (OptionsValidationException exception)
        {
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(MigrateCommand));

            var failures = string.Join("; ", exception.Failures);

            LogInvalidConfiguration(logger, failures);

            return InvalidConfiguration;
        }
    }

    private static int ExitCodeFor(MigrationStatus status)
    {
        return status switch
        {
            MigrationStatus.Succeeded => Succeeded,
            MigrationStatus.ScriptFailed => ScriptFailed,
            MigrationStatus.ConnectionFailed => ConnectionFailed,
            MigrationStatus.LockTimedOut => LockTimedOut,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown migration status.")
        };
    }

    [LoggerMessage(EventId = 5020, Level = LogLevel.Critical,
        Message = "Invalid configuration for the migration command: {Failures}")]
    private static partial void LogInvalidConfiguration(ILogger logger, string failures);
}
