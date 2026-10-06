using System.Reflection;
using Ledger.Application.Audit;
using Ledger.Domain.Accounts;

namespace Ledger.Application.Tests.Audit;

[Trait("Category", "Unit")]
public sealed class AuditEventsTests
{
    private const string CorrelationId = "5d1b7c0e9a3f4c28b6e1d04f7a92c3b8";
    private const string PassId = "a1b2c3d4e5f60718293a4b5c6d7e8f90";
    private const string Route = "POST /v1/accounts/{accountId}/entries";

    private static readonly AccountId Account = AccountId.From(Guid.Parse("0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33")).Value;

    [Fact]
    public void AccountCreated_ProducesTheCatalogEntryWithEmptyDetails()
    {
        var auditEvent = AuditEvents.AccountCreated("pix-core", Account, CorrelationId);

        auditEvent.EventType.ShouldBe("account.created");
        auditEvent.Outcome.ShouldBe("SUCCESS");
        auditEvent.ClientId.ShouldBe("pix-core");
        auditEvent.AccountId.ShouldBe(Account);
        auditEvent.CorrelationId.ShouldBe(CorrelationId);
        auditEvent.DetailsJson.ShouldBe("{}");
    }

    [Theory]
    [InlineData(DeniedWriteReason.InsufficientScope, "insufficient_scope")]
    [InlineData(DeniedWriteReason.NotProvisioningClient, "not_provisioning_client")]
    public void AuthorizationDenied_ProducesTheDeniedEntryWithTheRouteTheScopeAndTheReason(
        DeniedWriteReason reason,
        string reasonText)
    {
        var auditEvent = AuditEvents.AuthorizationDenied("report-reader", Account, CorrelationId, Route, reason);

        auditEvent.EventType.ShouldBe("authorization.denied_write");
        auditEvent.Outcome.ShouldBe("DENIED");
        auditEvent.ClientId.ShouldBe("report-reader");
        auditEvent.AccountId.ShouldBe(Account);
        auditEvent.DetailsJson.ShouldBe(
            "{\"route\":\"POST /v1/accounts/{accountId}/entries\",\"requiredScope\":\"ledger.write\",\"reason\":\""
            + reasonText + "\"}");
    }

    [Fact]
    public void AuthorizationDenied_WithoutAValidAccountInTheRoute_LeavesTheAccountNull()
    {
        var auditEvent = AuditEvents.AuthorizationDenied(
            "report-reader",
            null,
            CorrelationId,
            "POST /v1/accounts",
            DeniedWriteReason.NotProvisioningClient);

        auditEvent.AccountId.ShouldBeNull();
    }

    [Theory]
    [InlineData("POST /v1/accounts")]
    [InlineData("PUT /v1/accounts/{accountId}")]
    [InlineData("PATCH /v1/accounts/{accountId}/entries/{entryId}")]
    [InlineData("DELETE /v1/accounts/{accountId}/entries/{entryId}/reversals")]
    public void AuthorizationDenied_AcceptsRouteTemplatesOfWriteMethods(string route)
    {
        var auditEvent = AuditEvents.AuthorizationDenied(
            "client",
            null,
            CorrelationId,
            route,
            DeniedWriteReason.InsufficientScope);

        auditEvent.DetailsJson.ShouldContain(route);
    }

    [Theory]
    [InlineData("POST /v1/accounts/123.456.789-09/entries")]
    [InlineData("POST /v1/accounts/0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33/entries")]
    [InlineData("POST /v1/accounts/{accountId}/entries\nGET /secret")]
    [InlineData("POST /v1/accounts/{accountId}/entries\n")]
    [InlineData("GET /v1/accounts/{accountId}/balance")]
    [InlineData("POST v1/accounts")]
    [InlineData("POST /")]
    [InlineData("")]
    public void AuthorizationDenied_RefusesAnythingThatIsNotARouteTemplate(string route)
    {
        Should.Throw<ArgumentException>(() => AuditEvents.AuthorizationDenied(
            "client",
            null,
            CorrelationId,
            route,
            DeniedWriteReason.InsufficientScope));
    }

    [Fact]
    public void AuthorizationDenied_RefusesARouteLongerThanOneHundredAndTwentyCharacters()
    {
        var route = "POST /" + new string('a', 121);

        Should.Throw<ArgumentException>(() => AuditEvents.AuthorizationDenied(
            "client",
            null,
            CorrelationId,
            route,
            DeniedWriteReason.InsufficientScope));
    }

    [Fact]
    public void AuthorizationDenied_RefusesAnUnknownReason()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => AuditEvents.AuthorizationDenied(
            "client",
            null,
            CorrelationId,
            Route,
            (DeniedWriteReason)0));
    }

    [Fact]
    public void PiiDecrypted_ProducesTheWorkerEntryWithPurposeAndCount()
    {
        var auditEvent = AuditEvents.PiiDecrypted(PassId, 500);

        auditEvent.EventType.ShouldBe("pii.decrypted");
        auditEvent.Outcome.ShouldBe("SUCCESS");
        auditEvent.ClientId.ShouldBe("ledger-worker");
        auditEvent.AccountId.ShouldBeNull();
        auditEvent.CorrelationId.ShouldBe(PassId);
        auditEvent.DetailsJson.ShouldBe("{\"purpose\":\"rewrap\",\"accounts\":500}");
    }

    [Fact]
    public void PiiRewrapped_ProducesTheWorkerEntryWithVersionsAndCounts()
    {
        var auditEvent = AuditEvents.PiiRewrapped(PassId, 1, 2, 500, 3);

        auditEvent.EventType.ShouldBe("pii.rewrapped");
        auditEvent.Outcome.ShouldBe("SUCCESS");
        auditEvent.ClientId.ShouldBe("ledger-worker");
        auditEvent.AccountId.ShouldBeNull();
        auditEvent.DetailsJson.ShouldBe("{\"fromVersion\":1,\"toVersion\":2,\"accounts\":500,\"failed\":3}");
    }

    [Fact]
    public void KeysVersionActivated_ProducesTheWorkerEntryWithTheVersion()
    {
        var auditEvent = AuditEvents.KeysVersionActivated(PassId, 2);

        auditEvent.EventType.ShouldBe("keys.version_activated");
        auditEvent.Outcome.ShouldBe("SUCCESS");
        auditEvent.ClientId.ShouldBe("ledger-worker");
        auditEvent.AccountId.ShouldBeNull();
        auditEvent.CorrelationId.ShouldBe(PassId);
        auditEvent.DetailsJson.ShouldBe("{\"version\":2}");
    }

    [Fact]
    public void AuditEvent_HasNoPublicConstructor()
    {
        typeof(AuditEvent).GetConstructors(BindingFlags.Public | BindingFlags.Instance).ShouldBeEmpty();
    }

    [Fact]
    public void AuditEvent_IsSealed()
    {
        typeof(AuditEvent).IsSealed.ShouldBeTrue();
    }

    [Fact]
    public void ToString_OfAnEvent_PrintsOnlyTheTypeAndTheOutcome()
    {
        var auditEvent = AuditEvents.AuthorizationDenied(
            "client",
            Account,
            CorrelationId,
            Route,
            DeniedWriteReason.InsufficientScope);

        var text = auditEvent.ToString();

        text.ShouldContain("authorization.denied_write");
        text.ShouldNotContain(Route);
        text.ShouldNotContain(Account.ToString());
    }

    [Fact]
    public void EventTypes_AreTheOnesOfTheCatalog()
    {
        var types = typeof(AuditEventTypes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (string?)field.GetRawConstantValue())
            .Order()
            .ToList();

        types.ShouldBe(
        [
            "account.created",
            "authorization.denied_write",
            "integrity.run_completed",
            "integrity.violation_detected",
            "keys.version_activated",
            "pii.decrypted",
            "pii.rewrapped"
        ]);
    }

    [Theory]
    [InlineData(true, "SUCCESS")]
    [InlineData(false, "FAILURE")]
    public void IntegrityRunCompleted_ProducesTheWorkerEntryWithTheOutcomeOfTheRun(bool withoutViolations, string outcome)
    {
        var auditEvent = AuditEvents.IntegrityRunCompleted(PassId, withoutViolations, "{\"mode\":\"recent\"}");

        auditEvent.EventType.ShouldBe("integrity.run_completed");
        auditEvent.Outcome.ShouldBe(outcome);
        auditEvent.ClientId.ShouldBe(AuditEvents.WorkerClientId);
        auditEvent.AccountId.ShouldBeNull();
        auditEvent.CorrelationId.ShouldBe(PassId);
        auditEvent.DetailsJson.ShouldBe("{\"mode\":\"recent\"}");
    }

    [Fact]
    public void IntegrityViolationDetected_ProducesAFailureEntryForTheAccount()
    {
        var auditEvent = AuditEvents.IntegrityViolationDetected(PassId, Account, "{\"check\":\"BalanceMatchesEntries\"}");

        auditEvent.EventType.ShouldBe("integrity.violation_detected");
        auditEvent.Outcome.ShouldBe("FAILURE");
        auditEvent.ClientId.ShouldBe(AuditEvents.WorkerClientId);
        auditEvent.AccountId.ShouldBe(Account);
        auditEvent.CorrelationId.ShouldBe(PassId);
    }
}
