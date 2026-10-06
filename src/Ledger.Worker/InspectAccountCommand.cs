using Ledger.Application.Integrity;
using Ledger.Domain.Accounts;
using Microsoft.Extensions.Options;

namespace Ledger.Worker;

internal static partial class InspectAccountCommand
{
    public const string Argument = "--inspect-account";

    private const int Clean = 0;
    private const int FindingsReported = 1;
    private const int InvalidInput = 3;

    public static async Task<int> RunAsync(
        IServiceProvider services,
        string? accountText,
        CancellationToken cancellationToken)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(InspectAccountCommand));

        if (!Guid.TryParse(accountText, out var guid) || AccountId.From(guid) is not { IsSuccess: true } parsed)
        {
            LogInvalidAccount(logger);

            return InvalidInput;
        }

        try
        {
            using var scope = services.CreateScope();

            var report = await scope.ServiceProvider
                .GetRequiredService<InspectAccountIntegrityHandler>()
                .HandleAsync(new InspectAccountIntegrityCommand(parsed.Value), cancellationToken);

            return Report(logger, report);
        }
        catch (OptionsValidationException exception)
        {
            var failures = string.Join("; ", exception.Failures);

            LogInvalidConfiguration(logger, failures);

            return InvalidInput;
        }
    }

    private static int Report(ILogger logger, AccountIntegrityReport report)
    {
        LogInspected(logger, report.AccountId, report.EntryCount, report.Findings.Count);

        foreach (var finding in report.Findings)
        {
            var check = finding.Check.ToString();
            var entryId = finding.EntryId?.ToString();

            LogFinding(logger, report.AccountId, check, entryId, finding.AccountVersion);
        }

        return report.Findings.Count == 0 ? Clean : FindingsReported;
    }

    [LoggerMessage(
        EventId = 4006,
        EventName = "AccountIntegrityInspected",
        Level = LogLevel.Information,
        Message = "Account {AccountId} inspected: {EntryCount} entries and {Findings} findings")]
    private static partial void LogInspected(ILogger logger, AccountId accountId, long entryCount, int findings);

    [LoggerMessage(
        EventId = 4007,
        EventName = "AccountIntegrityFinding",
        Level = LogLevel.Error,
        Message = "Account {AccountId} fails {Check} (entry {EntryId}, version {AccountVersion})")]
    private static partial void LogFinding(
        ILogger logger,
        AccountId accountId,
        string check,
        string? entryId,
        long? accountVersion);

    [LoggerMessage(
        EventId = 5022,
        Level = LogLevel.Critical,
        Message = "The account to inspect must be informed as --inspect-account=<guid>")]
    private static partial void LogInvalidAccount(ILogger logger);

    [LoggerMessage(
        EventId = 5023,
        Level = LogLevel.Critical,
        Message = "Invalid configuration for the inspection command: {Failures}")]
    private static partial void LogInvalidConfiguration(ILogger logger, string failures);
}
