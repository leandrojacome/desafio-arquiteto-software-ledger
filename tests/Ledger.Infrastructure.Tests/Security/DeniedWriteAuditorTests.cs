using System.Text.Json;
using Ledger.Application.Abstractions;
using Ledger.Application.Audit;
using Ledger.Application.Tests.Security;
using Ledger.Application.Tests.Support;
using Ledger.Domain.Accounts;
using Ledger.Infrastructure.Audit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Ledger.Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public sealed class DeniedWriteAuditorTests
{
    private const string Route = "POST /v1/accounts/{accountId}/entries";
    private const string Client = "pix-core";

    private FakeTimeProvider Time { get; } = new();

    private IAuditTrail Trail { get; } = Substitute.For<IAuditTrail>();

    private ISecurityTelemetry Telemetry { get; } = Substitute.For<ISecurityTelemetry>();

    private CapturingLogger<DeniedWriteAuditor> Logger { get; } = new();

    private List<AuditEvent> Recorded { get; } = [];

    private List<CancellationToken> Tokens { get; } = [];

    private DeniedWriteAuditor Auditor(int capacity = 10, int refillPerSecond = 1, TimeProvider? time = null)
    {
        Trail.RecordAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                lock (Recorded)
                {
                    Recorded.Add(call.Arg<AuditEvent>());
                    Tokens.Add(call.Arg<CancellationToken>());
                }

                return Task.CompletedTask;
            });

        var options = Options.Create(new DeniedWriteAuditOptions { Capacity = capacity, RefillPerSecond = refillPerSecond });

        return new DeniedWriteAuditor(Trail, Telemetry, options, time ?? Time, Logger);
    }

    private int RecordedCount()
    {
        lock (Recorded)
        {
            return Recorded.Count;
        }
    }

    private static void Deny(DeniedWriteAuditor auditor, string client = Client, string route = Route)
    {
        auditor.Record(
            client,
            AccountId.From(Guid.Parse(SecurityVectors.AccountText)).Value,
            "corr-1",
            route,
            DeniedWriteReason.InsufficientScope);
    }

    [Fact]
    public async Task Record_TenDenialsInARow_AreAllRecordedAndTheEleventhIsNot()
    {
        var auditor = Auditor();

        for (var count = 0; count < 11; count++)
        {
            Deny(auditor);
        }

        await EventuallyTrue.WaitAsync(() => RecordedCount() == 10 && auditor.PendingWrites == 0, "ten writes");
        RecordedCount().ShouldBe(10);
        Telemetry.Received(1).AuditSkipped("rate_capped");
        Telemetry.DidNotReceive().AuditSkipped("write_failed");
    }

    [Fact]
    public void Record_TheDenialBeyondTheCap_LogsTheWarningWithTheClientOnly()
    {
        var auditor = Auditor(capacity: 1);

        Deny(auditor);
        Deny(auditor);

        var log = Logger.Single(6003);
        log.Level.ShouldBe(LogLevel.Warning);
        log.Properties["Reason"].ShouldBe("rate_capped");
        log.Properties["ClientId"].ShouldBe(Client);
        log.Message.ShouldNotContain(Route, Case.Sensitive);
    }

    [Fact]
    public async Task Record_OneTokenComesBackEverySecond()
    {
        var auditor = Auditor();

        for (var count = 0; count < 11; count++)
        {
            Deny(auditor);
        }

        await EventuallyTrue.WaitAsync(() => RecordedCount() == 10, "first burst");

        Time.Advance(TimeSpan.FromSeconds(1));
        Deny(auditor);
        Deny(auditor);

        await EventuallyTrue.WaitAsync(() => RecordedCount() == 11, "one refilled token");
        RecordedCount().ShouldBe(11);
        Telemetry.Received(2).AuditSkipped("rate_capped");
    }

    [Fact]
    public async Task Record_RefillIsCappedAtTheCapacity()
    {
        var auditor = Auditor(capacity: 3, refillPerSecond: 2);

        Deny(auditor);
        Time.Advance(TimeSpan.FromMinutes(10));

        for (var count = 0; count < 5; count++)
        {
            Deny(auditor);
        }

        await EventuallyTrue.WaitAsync(() => RecordedCount() == 4 && auditor.PendingWrites == 0, "capacity bound");
        Telemetry.Received(2).AuditSkipped("rate_capped");
    }

    [Fact]
    public async Task Record_TwoClients_DoNotShareABucket()
    {
        var auditor = Auditor(capacity: 2);

        for (var count = 0; count < 3; count++)
        {
            Deny(auditor, "client-a");
        }

        for (var count = 0; count < 2; count++)
        {
            Deny(auditor, "client-b");
        }

        await EventuallyTrue.WaitAsync(() => RecordedCount() == 4 && auditor.PendingWrites == 0, "two buckets");
        Recorded.Count(recorded => recorded.ClientId == "client-a").ShouldBe(2);
        Recorded.Count(recorded => recorded.ClientId == "client-b").ShouldBe(2);
        Telemetry.Received(1).AuditSkipped("rate_capped");
    }

    [Fact]
    public async Task Record_ReturnsWithoutWaitingForTheWrite()
    {
        var release = new TaskCompletionSource();
        var auditor = Auditor();
        Trail.RecordAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>()).Returns(_ => release.Task);

        Deny(auditor);

        auditor.PendingWrites.ShouldBe(1);
        release.SetResult();
        await EventuallyTrue.WaitAsync(() => auditor.PendingWrites == 0, "write released");
    }

    [Fact]
    public async Task Record_TrailThatThrowsAsynchronously_IsCapturedAndCounted()
    {
        var auditor = Auditor();
        Trail.RecordAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database is down"));

        Should.NotThrow(() => Deny(auditor));

        await EventuallyTrue.WaitAsync(() => auditor.PendingWrites == 0, "failed write finished");
        Telemetry.Received(1).AuditSkipped("write_failed");
        var log = Logger.Single(6004);
        log.Level.ShouldBe(LogLevel.Warning);
        log.Properties["ExceptionType"].ShouldBe(nameof(InvalidOperationException));
        log.Message.ShouldNotContain("database is down", Case.Sensitive);
    }

    [Fact]
    public async Task Record_TrailThatThrowsSynchronously_IsCapturedAndCounted()
    {
        var auditor = Auditor();
        Trail.RecordAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>())
            .Throws(new TimeoutException("pool exhausted"));

        Should.NotThrow(() => Deny(auditor));

        await EventuallyTrue.WaitAsync(() => auditor.PendingWrites == 0, "failed write finished");
        Telemetry.Received(1).AuditSkipped("write_failed");
        Logger.Single(6004).Properties["ExceptionType"].ShouldBe(nameof(TimeoutException));
    }

    [Fact]
    public async Task Record_SuccessfulWrite_PassesTheDenialEventOfTheCatalog()
    {
        var auditor = Auditor();

        Deny(auditor);

        await EventuallyTrue.WaitAsync(() => RecordedCount() == 1, "one write");
        var auditEvent = Recorded.Single();
        auditEvent.EventType.ShouldBe("authorization.denied_write");
        auditEvent.Outcome.ShouldBe("DENIED");
        auditEvent.ClientId.ShouldBe(Client);
        auditEvent.CorrelationId.ShouldBe("corr-1");
        auditEvent.AccountId.ShouldBe(AccountId.From(Guid.Parse(SecurityVectors.AccountText)).Value);

        using var details = JsonDocument.Parse(auditEvent.DetailsJson);
        details.RootElement.GetProperty("route").GetString().ShouldBe(Route);
        details.RootElement.GetProperty("requiredScope").GetString().ShouldBe("ledger.write");
        details.RootElement.GetProperty("reason").GetString().ShouldBe("insufficient_scope");
        Telemetry.DidNotReceive().AuditSkipped(Arg.Any<string>());
    }

    [Fact]
    public async Task Record_NotProvisioningClient_UsesItsOwnReason()
    {
        var auditor = Auditor();

        auditor.Record(Client, null, "corr-2", "POST /v1/accounts", DeniedWriteReason.NotProvisioningClient);

        await EventuallyTrue.WaitAsync(() => RecordedCount() == 1, "one write");
        var auditEvent = Recorded.Single();
        auditEvent.AccountId.ShouldBeNull();
        using var details = JsonDocument.Parse(auditEvent.DetailsJson);
        details.RootElement.GetProperty("reason").GetString().ShouldBe("not_provisioning_client");
    }

    [Fact]
    public async Task Record_RealPathInsteadOfTheTemplate_NeverReachesTheTrailAndIsCountedAsAFailure()
    {
        var auditor = Auditor();

        Should.NotThrow(() =>
            Deny(auditor, route: "POST /v1/accounts/0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33/entries"));

        await EventuallyTrue.WaitAsync(() => auditor.PendingWrites == 0, "rejected route handled");
        RecordedCount().ShouldBe(0);
        Telemetry.Received(1).AuditSkipped("write_failed");
        Logger.Single(6004).Properties["ExceptionType"].ShouldBe(nameof(ArgumentException));
    }

    [Fact]
    public async Task Record_WriteRunsWithItsOwnDeadlineAndIsNotCancelledByTheCaller()
    {
        var auditor = Auditor();

        Deny(auditor);

        await EventuallyTrue.WaitAsync(() => RecordedCount() == 1, "one write");
        var token = Tokens.Single();
        token.CanBeCanceled.ShouldBeTrue();
        token.IsCancellationRequested.ShouldBeFalse();
    }

    [Fact]
    public async Task Record_WriteThatNeverEnds_IsCutByItsOwnDeadline()
    {
        var release = new TaskCompletionSource();
        CancellationToken captured = default;
        var auditor = Auditor();
        Trail.RecordAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                captured = call.Arg<CancellationToken>();

                return release.Task;
            });

        Deny(auditor);
        await EventuallyTrue.WaitAsync(() => captured.CanBeCanceled, "write started");
        Time.Advance(TimeSpan.FromSeconds(6));

        captured.IsCancellationRequested.ShouldBeTrue();
        release.SetResult();
        await EventuallyTrue.WaitAsync(() => auditor.PendingWrites == 0, "write released");
    }

    [Fact]
    public async Task Record_ClientsIdleForAnHour_AreForgotten()
    {
        var auditor = Auditor();

        Deny(auditor, "client-a");
        Deny(auditor, "client-b");
        auditor.TrackedClients.ShouldBe(2);
        await EventuallyTrue.WaitAsync(() => auditor.PendingWrites == 0, "writes finished");

        Time.Advance(TimeSpan.FromMinutes(61));
        Deny(auditor, "client-c");

        auditor.TrackedClients.ShouldBe(1);
    }

    [Fact]
    public async Task Record_ClientActiveWithinTheHour_KeepsItsBucket()
    {
        var auditor = Auditor();

        Deny(auditor, "client-a");
        Time.Advance(TimeSpan.FromMinutes(30));
        Deny(auditor, "client-a");
        Time.Advance(TimeSpan.FromMinutes(40));
        Deny(auditor, "client-b");

        auditor.TrackedClients.ShouldBe(2);
        await EventuallyTrue.WaitAsync(() => auditor.PendingWrites == 0, "writes finished");
    }

    [Fact]
    public async Task DisposeAsync_WaitsForTheWritesInFlight()
    {
        var release = new TaskCompletionSource();
        var auditor = Auditor(time: TimeProvider.System);
        Trail.RecordAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>()).Returns(_ => release.Task);

        Deny(auditor);
        var disposing = auditor.DisposeAsync().AsTask();

        await Should.ThrowAsync<TimeoutException>(() => disposing.WaitAsync(TimeSpan.FromMilliseconds(150)));
        release.SetResult();
        await disposing.WaitAsync(TimeSpan.FromSeconds(10));
        auditor.PendingWrites.ShouldBe(0);
    }

    [Fact]
    public async Task Record_NeverLogsTheRouteTheTokenOrTheBody()
    {
        var auditor = Auditor(capacity: 1);

        Deny(auditor);
        Deny(auditor);
        await EventuallyTrue.WaitAsync(() => auditor.PendingWrites == 0, "writes finished");

        Logger.Entries.ShouldAllBe(entry => !entry.Message.Contains("/v1/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1001, 1)]
    [InlineData(10, 0)]
    [InlineData(10, 1001)]
    public void Options_OutOfRange_FailNamingTheKey(int capacity, int refill)
    {
        var result = new DeniedWriteAuditOptionsValidator()
            .Validate(null, new DeniedWriteAuditOptions { Capacity = capacity, RefillPerSecond = refill });

        result.Failed.ShouldBeTrue();
        result.Message().ShouldContain("Security:Audit:DeniedWrite", Case.Sensitive);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1000, 1000)]
    [InlineData(10, 1)]
    public void Options_InRange_Pass(int capacity, int refill)
    {
        new DeniedWriteAuditOptionsValidator()
            .Validate(null, new DeniedWriteAuditOptions { Capacity = capacity, RefillPerSecond = refill })
            .Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Options_Defaults_AreTenAndOnePerSecond()
    {
        var defaults = new DeniedWriteAuditOptions();

        defaults.Capacity.ShouldBe(10);
        defaults.RefillPerSecond.ShouldBe(1);
    }
}
