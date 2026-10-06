using System.Diagnostics;
using Ledger.Application.Abstractions;
using Ledger.Infrastructure.Observability.Labels;

namespace Ledger.Infrastructure.Observability;

internal sealed class SecurityTelemetry(TelemetrySources sources, LedgerMeters meters, KeyUsageHolder keyUsage)
    : ISecurityTelemetry
{
    public void AuthFailed(string reason)
    {
        if (LabelTable<AuthFailureReason>.TryParse(reason, out var parsed))
        {
            meters.AuthFailures.Add(1, new TagList { { TagKeys.Reason, parsed.Label() } });
        }
    }

    public void RateLimitRejected(string policy)
    {
        if (LabelTable<RateLimitPolicy>.TryParse(policy, out var parsed))
        {
            meters.RateLimitRejections.Add(1, new TagList { { TagKeys.Policy, parsed.Label() } });
        }
    }

    public void AccountCreated()
    {
        meters.AccountsCreated.Add(1);
    }

    public IAccountCreationOperation BeginCreateAccount()
    {
        var activity = sources.Source.StartActivity(SpanNames.CreateAccount, ActivityKind.Internal);

        return new AccountCreationOperation(activity);
    }

    public void PiiDecrypted(string purpose, int accounts)
    {
        if (accounts > 0 && LabelTable<PiiPurpose>.TryParse(purpose, out var parsed))
        {
            meters.PiiDecrypt.Add(accounts, new TagList { { TagKeys.Purpose, parsed.Label() } });
        }
    }

    public void KeyReloaded(bool succeeded)
    {
        var result = succeeded ? ReloadResult.Ok : ReloadResult.Failed;

        meters.KeyReloads.Add(1, new TagList { { TagKeys.Result, result.Label() } });
    }

    public void AuditRecorded(string eventType, string outcome)
    {
        if (LabelTable<AuditEventLabel>.TryParse(eventType, out var parsedType)
            && LabelTable<AuditOutcomeLabel>.TryParse(outcome, out var parsedOutcome))
        {
            meters.AuditRecorded.Add(
                1,
                new TagList { { TagKeys.EventType, parsedType.Label() }, { TagKeys.Outcome, parsedOutcome.Label() } });
        }
    }

    public void AuditSkipped(string reason)
    {
        if (LabelTable<AuditSkipReason>.TryParse(reason, out var parsed))
        {
            meters.AuditSkipped.Add(1, new TagList { { TagKeys.Reason, parsed.Label() } });
        }
    }

    public IRewrapBatchOperation BeginRewrapBatch()
    {
        var activity = sources.Source.StartActivity(SpanNames.RewrapBatch, ActivityKind.Internal);

        return new RewrapBatchOperation(activity);
    }

    public void KeyUsageObserved(long accountsBelowActive, bool anomalous)
    {
        keyUsage.Record(accountsBelowActive, anomalous);
    }

    public void AccountsRewrapped(int rewrapped, int failed)
    {
        if (rewrapped > 0)
        {
            meters.RewrapAccounts.Add(rewrapped, new TagList { { TagKeys.Result, RewrapResult.Rewrapped.Label() } });
        }

        if (failed > 0)
        {
            meters.RewrapAccounts.Add(failed, new TagList { { TagKeys.Result, RewrapResult.Failed.Label() } });
        }
    }
}
