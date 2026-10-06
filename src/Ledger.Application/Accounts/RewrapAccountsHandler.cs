using System.Globalization;
using Ledger.Application.Abstractions;
using Ledger.Application.Security;
using Ledger.Domain.Accounts;
using Ledger.Domain.Shared;
using Microsoft.Extensions.Logging;

namespace Ledger.Application.Accounts;

public sealed class RewrapAccountsHandler(
    IAccountKeyRewrapper rewrapper,
    IHolderDocumentProtector protector,
    IKeyProvider keyProvider,
    ISecurityTelemetry telemetry,
    ILogger<RewrapAccountsHandler> logger)
{
    private const string RewrapPurpose = "rewrap";

    public async Task<RewrapPassResult> HandleAsync(RewrapAccountsCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var pass = await rewrapper.TryBeginPassAsync(cancellationToken);

        if (pass is null)
        {
            return RewrapPassResult.SkippedPass;
        }

        AccountId? after = null;
        var rewrapped = 0;
        var failed = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var request = new RewrapBatchRequest(command.ActiveVersion, after, command.BatchSize, command.PassId);
            RewrapBatchResult batch;

            using (var operation = telemetry.BeginRewrapBatch())
            {
                batch = await rewrapper.RewrapBatchAsync(request, Transform, cancellationToken);
                operation.Complete(batch.Rewrapped, batch.Failed);
            }

            if (batch.Selected == 0)
            {
                break;
            }

            telemetry.PiiDecrypted(RewrapPurpose, batch.Rewrapped);
            telemetry.AccountsRewrapped(batch.Rewrapped, batch.Failed);
            AccountLog.RewrapBatchCompleted(logger, batch.Rewrapped, batch.Failed, command.ActiveVersion);

            rewrapped += batch.Rewrapped;
            failed += batch.Failed;
            after = batch.LastId ?? throw new InvalidOperationException("A non empty rewrap batch must report its last id.");
        }

        var usageIsClean = await ObserveKeyUsageAsync(command.ActiveVersion, cancellationToken);

        return new RewrapPassResult(rewrapped, failed, rewrapped + failed == 0 && usageIsClean);
    }

    private static string Join(IEnumerable<int> versions) =>
        string.Join(',', versions.Select(version => version.ToString(CultureInfo.InvariantCulture)));

    private async Task<bool> ObserveKeyUsageAsync(int activeVersion, CancellationToken cancellationToken)
    {
        var usage = await rewrapper.ReadKeyUsageAsync(cancellationToken);
        var readable = keyProvider.Live.Select(set => (int)set.Version).ToHashSet();

        var below = usage.AccountsBelow(activeVersion);
        var ahead = usage.VersionsAbove(activeVersion);
        var unreadable = usage.VersionsOutside(readable);

        if (ahead.Count > 0)
        {
            AccountLog.KeyVersionAheadOfActive(logger, Join(ahead), activeVersion);
        }

        if (unreadable.Count > 0)
        {
            AccountLog.KeyVersionNotLive(logger, Join(unreadable));
        }

        var anomalous = ahead.Count > 0 || unreadable.Count > 0;

        telemetry.KeyUsageObserved(below, anomalous);

        return below == 0 && !anomalous;
    }

    private Result<ProtectedHolderDocument> Transform(RewrapRow row)
    {
        var result = protector.Reprotect(row.Encrypted, row.Id);

        if (result.IsFailure)
        {
            AccountLog.DocumentDecryptFailed(logger, row.Id);
        }

        return result;
    }
}
