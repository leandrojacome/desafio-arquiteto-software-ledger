using System.Diagnostics;
using Ledger.Infrastructure.Tests.Observability.Support;

namespace Ledger.Infrastructure.Tests.Observability;

[Trait("Category", "Unit")]
public sealed class SecurityTelemetryTests : IDisposable
{
    private readonly TestTelemetry _telemetry = new();

    public void Dispose()
    {
        _telemetry.Dispose();
    }

    [Theory]
    [InlineData("missing_token")]
    [InlineData("invalid_token")]
    [InlineData("expired")]
    [InlineData("insufficient_scope")]
    [InlineData("missing_client_id")]
    [InlineData("invalid_client_id")]
    public void AuthFailed_CountsExactlyOneFailureWithTheReasonAndNoClient(string reason)
    {
        _telemetry.Security.AuthFailed(reason);

        var measurement = _telemetry.Capture.Of("ledger.auth.failures").ShouldHaveSingleItem();
        measurement.Value.ShouldBe(1);
        measurement.Unit.ShouldBe("{failure}");
        measurement.Tags.Keys.ShouldBe(["reason"]);
        measurement.Tag("reason").ShouldBe(reason);
    }

    [Theory]
    [InlineData("write-per-client")]
    [InlineData("read-per-client")]
    [InlineData("write-per-account")]
    [InlineData("write-concurrency")]
    [InlineData("balance-concurrency")]
    [InlineData("statement-concurrency")]
    public void RateLimitRejected_CountsExactlyOneRejectionWithThePolicy(string policy)
    {
        _telemetry.Security.RateLimitRejected(policy);

        var measurement = _telemetry.Capture.Of("ledger.rate_limit.rejections").ShouldHaveSingleItem();
        measurement.Value.ShouldBe(1);
        measurement.Unit.ShouldBe("{rejection}");
        measurement.Tags.Keys.ShouldBe(["policy"]);
        measurement.Tag("policy").ShouldBe(policy);
    }

    [Fact]
    public void AccountCreated_CountsOneAccountWithoutLabels()
    {
        _telemetry.Security.AccountCreated();

        var measurement = _telemetry.Capture.Of("ledger.accounts.created").ShouldHaveSingleItem();
        measurement.Value.ShouldBe(1);
        measurement.Unit.ShouldBe("{account}");
        measurement.Tags.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("rewrap", 40)]
    [InlineData("holder_lookup", 1)]
    [InlineData("investigation", 7)]
    public void PiiDecrypted_AddsTheAccountsWithThePurpose(string purpose, int accounts)
    {
        _telemetry.Security.PiiDecrypted(purpose, accounts);

        var measurement = _telemetry.Capture.Of("ledger.pii.decrypt").ShouldHaveSingleItem();
        measurement.Value.ShouldBe(accounts);
        measurement.Unit.ShouldBe("{account}");
        measurement.Tags.Keys.ShouldBe(["purpose"]);
        measurement.Tag("purpose").ShouldBe(purpose);
    }

    [Fact]
    public void PiiDecrypted_WithNoAccounts_CountsNothing()
    {
        _telemetry.Security.PiiDecrypted("rewrap", 0);

        _telemetry.Capture.Of("ledger.pii.decrypt").ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true, "ok")]
    [InlineData(false, "failed")]
    public void KeyReloaded_CountsOneReloadWithTheResult(bool succeeded, string result)
    {
        _telemetry.Security.KeyReloaded(succeeded);

        var measurement = _telemetry.Capture.Of("ledger.key.reloads").ShouldHaveSingleItem();
        measurement.Value.ShouldBe(1);
        measurement.Unit.ShouldBe("{reload}");
        measurement.Tags.Keys.ShouldBe(["result"]);
        measurement.Tag("result").ShouldBe(result);
    }

    [Theory]
    [InlineData("account.created", "SUCCESS")]
    [InlineData("authorization.denied_write", "DENIED")]
    [InlineData("pii.decrypted", "SUCCESS")]
    [InlineData("pii.rewrapped", "SUCCESS")]
    [InlineData("keys.version_activated", "SUCCESS")]
    public void AuditRecorded_CountsOneEventWithTypeAndOutcome(string eventType, string outcome)
    {
        _telemetry.Security.AuditRecorded(eventType, outcome);

        var measurement = _telemetry.Capture.Of("ledger.audit.recorded").ShouldHaveSingleItem();
        measurement.Value.ShouldBe(1);
        measurement.Unit.ShouldBe("{event}");
        measurement.Tags.Keys.OrderBy(key => key).ShouldBe(["event_type", "outcome"]);
        measurement.Has(("event_type", eventType), ("outcome", outcome)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("account.created", "NOPE")]
    [InlineData("account.deleted", "SUCCESS")]
    public void AuditRecorded_WithAValueOutsideTheCatalog_CountsNothing(string eventType, string outcome)
    {
        _telemetry.Security.AuditRecorded(eventType, outcome);

        _telemetry.Capture.Of("ledger.audit.recorded").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("rate_capped")]
    [InlineData("write_failed")]
    public void AuditSkipped_CountsOneEventWithTheReason(string reason)
    {
        _telemetry.Security.AuditSkipped(reason);

        var measurement = _telemetry.Capture.Of("ledger.audit.skipped").ShouldHaveSingleItem();
        measurement.Value.ShouldBe(1);
        measurement.Tags.Keys.ShouldBe(["reason"]);
        measurement.Tag("reason").ShouldBe(reason);
    }

    [Fact]
    public void AccountsRewrapped_CountsTheTwoResultsSeparately()
    {
        _telemetry.Security.AccountsRewrapped(40, 3);

        _telemetry.Capture.Sum("ledger.rewrap.accounts", ("result", "rewrapped")).ShouldBe(40);
        _telemetry.Capture.Sum("ledger.rewrap.accounts", ("result", "failed")).ShouldBe(3);
        _telemetry.Capture.Of("ledger.rewrap.accounts").ShouldAllBe(measurement => measurement.Unit == "{account}");
    }

    [Fact]
    public void AccountsRewrapped_WithZeros_CountsNothing()
    {
        _telemetry.Security.AccountsRewrapped(0, 0);

        _telemetry.Capture.Of("ledger.rewrap.accounts").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("a total invention")]
    [InlineData("")]
    [InlineData("0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33")]
    public void EveryClosedLabel_WithAFreeTextValue_IsDropped(string text)
    {
        _telemetry.Security.AuthFailed(text);
        _telemetry.Security.RateLimitRejected(text);
        _telemetry.Security.PiiDecrypted(text, 5);
        _telemetry.Security.AuditSkipped(text);

        _telemetry.Capture.All.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("created", false)]
    [InlineData("already_created", false)]
    [InlineData("key_unavailable", true)]
    [InlineData("failed", true)]
    public void BeginCreateAccount_RecordsTheOutcomeOnAnInternalSpan(string outcome, bool isError)
    {
        using var request = TestActivities.Start("http.request");

        using (var operation = _telemetry.Security.BeginCreateAccount())
        {
            operation.Outcome(outcome);
        }

        var span = _telemetry.Capture.SingleActivity("ledger.create_account");
        span.Kind.ShouldBe(ActivityKind.Internal);
        span.Parent.ShouldBe(request);
        span.GetTagItem("ledger.outcome").ShouldBe(outcome);
        (span.Status == ActivityStatusCode.Error).ShouldBe(isError);
    }

    [Fact]
    public void BeginCreateAccount_WithoutAnOutcome_ReportsFailed()
    {
        using (_telemetry.Security.BeginCreateAccount())
        {
        }

        _telemetry.Capture.SingleActivity("ledger.create_account").GetTagItem("ledger.outcome").ShouldBe("failed");
    }

    [Fact]
    public void BeginCreateAccount_WithAnOutcomeOutsideTheSet_KeepsTheDefault()
    {
        using (var operation = _telemetry.Security.BeginCreateAccount())
        {
            operation.Outcome("holder 123.456.789-09 created");
        }

        _telemetry.Capture.SingleActivity("ledger.create_account").GetTagItem("ledger.outcome").ShouldBe("failed");
    }

    [Fact]
    public void BeginRewrapBatch_RecordsTheCountsOnAnInternalSpan()
    {
        using (var operation = _telemetry.Security.BeginRewrapBatch())
        {
            operation.Complete(498, 2);
        }

        var span = _telemetry.Capture.SingleActivity("pii.rewrap");
        span.Kind.ShouldBe(ActivityKind.Internal);
        span.GetTagItem("ledger.rewrap.accounts").ShouldBe(498);
        span.GetTagItem("ledger.rewrap.failed").ShouldBe(2);
    }
}
