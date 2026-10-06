using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Ledger.Application.Abstractions;
using Ledger.Application.Audit;
using Microsoft.Extensions.Logging;

namespace Ledger.Application.Security;

public sealed partial class RecordKeyActivationHandler(
    IKeyProvider keyProvider,
    IAuditTrail auditTrail,
    IIdGenerator idGenerator,
    ILogger<RecordKeyActivationHandler> logger)
{
    private const string VersionDetail = "version";

    public async Task<bool> HandleAsync(CancellationToken cancellationToken)
    {
        var version = keyProvider.Active.Version;

        if (await IsAlreadyRecordedAsync(version, cancellationToken))
        {
            return false;
        }

        var passId = idGenerator.NewId().ToString("N");
        await auditTrail.RecordAsync(AuditEvents.KeysVersionActivated(passId, version), cancellationToken);

        return true;
    }

    [LoggerMessage(
        EventId = 7008,
        EventName = "KeyActivationLookupFailed",
        Level = LogLevel.Warning,
        Message = "The audit trail could not be read to find the activation of key version {Version} ({ExceptionType})")]
    private static partial void LogLookupFailed(ILogger logger, int version, string exceptionType);

    [SuppressMessage("Design", "CA1031",
        Justification = "A failed lookup only risks a duplicate activation row, which is harmless in an append only trail.")]
    private async Task<bool> IsAlreadyRecordedAsync(ushort version, CancellationToken cancellationToken)
    {
        try
        {
            return await auditTrail.ContainsAsync(
                AuditEventTypes.KeysVersionActivated,
                VersionDetail,
                version.ToString(CultureInfo.InvariantCulture),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogLookupFailed(logger, version, exception.GetType().Name);

            return false;
        }
    }
}
