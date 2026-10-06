using Ledger.Application.Abstractions;
using Ledger.Application.Audit;
using Ledger.Application.Security;
using Ledger.Application.Tests.Support;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Ledger.Application.Tests.Security;

[Trait("Category", "Unit")]
public sealed class RecordKeyActivationHandlerTests
{
    private static readonly Guid PassGuid = Guid.Parse("a1b2c3d4-e5f6-0718-293a-4b5c6d7e8f90");

    private IKeyProvider Keys { get; } = Substitute.For<IKeyProvider>();

    private IAuditTrail Trail { get; } = Substitute.For<IAuditTrail>();

    private IIdGenerator Ids { get; } = Substitute.For<IIdGenerator>();

    private CapturingLogger<RecordKeyActivationHandler> Logger { get; } = new();

    private RecordKeyActivationHandler Handler => new(Keys, Trail, Ids, Logger);

    public RecordKeyActivationHandlerTests()
    {
        Keys.Active.Returns(new KeySet(2, new byte[32], new byte[32]));
        Ids.NewId().Returns(PassGuid);
    }

    private void ContainsReturns(bool contained) =>
        Trail.ContainsAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(contained);

    [Fact]
    public async Task HandleAsync_VersionNotYetRecorded_WritesTheActivationOnce()
    {
        ContainsReturns(false);

        var recorded = await Handler.HandleAsync(CancellationToken.None);

        recorded.ShouldBeTrue();
        var auditEvent = Trail.ReceivedCalls()
            .SelectMany(call => call.GetArguments())
            .OfType<AuditEvent>()
            .Single();
        auditEvent.EventType.ShouldBe("keys.version_activated");
        auditEvent.ClientId.ShouldBe("ledger-worker");
        auditEvent.CorrelationId.ShouldBe("a1b2c3d4e5f60718293a4b5c6d7e8f90");
        auditEvent.DetailsJson.ShouldBe("{\"version\":2}");
    }

    [Fact]
    public async Task HandleAsync_LooksTheVersionUpByTheEventTypeAndTheVersionDetail()
    {
        ContainsReturns(false);

        await Handler.HandleAsync(CancellationToken.None);

        await Trail.Received(1).ContainsAsync(
            "keys.version_activated",
            "version",
            "2",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_VersionAlreadyRecorded_DoesNotWriteAgain()
    {
        ContainsReturns(true);

        var recorded = await Handler.HandleAsync(CancellationToken.None);

        recorded.ShouldBeFalse();
        await Trail.DidNotReceive().RecordAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_LookupFails_LogsAWarningAndWritesAnyway()
    {
        Trail.ContainsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TimeoutException("permission denied for audit_log"));

        var recorded = await Handler.HandleAsync(CancellationToken.None);

        recorded.ShouldBeTrue();
        await Trail.Received(1).RecordAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>());
        var log = Logger.Single(7008);
        log.Level.ShouldBe(LogLevel.Warning);
        log.Properties["Version"].ShouldBe("2");
        log.Properties["ExceptionType"].ShouldBe(nameof(TimeoutException));
        log.Message.ShouldNotContain("permission denied");
    }

    [Fact]
    public async Task HandleAsync_LookupCanceled_PropagatesWithoutWriting()
    {
        Trail.ContainsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(() => Handler.HandleAsync(CancellationToken.None));

        await Trail.DidNotReceive().RecordAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_KeysUnavailable_PropagatesWithoutLookingAnythingUp()
    {
        Keys.Active.Throws(new KeyProviderUnavailableException());

        await Should.ThrowAsync<KeyProviderUnavailableException>(() => Handler.HandleAsync(CancellationToken.None));

        await Trail.DidNotReceive().ContainsAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WriteFails_TheExceptionPropagates()
    {
        ContainsReturns(false);
        Trail.RecordAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException("db"));

        await Should.ThrowAsync<TimeoutException>(() => Handler.HandleAsync(CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_TokenReachesTheTrail()
    {
        ContainsReturns(false);
        using var source = new CancellationTokenSource();

        await Handler.HandleAsync(source.Token);

        await Trail.Received(1).RecordAsync(Arg.Any<AuditEvent>(), source.Token);
        await Trail.Received(1).ContainsAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            source.Token);
    }

    [Fact]
    public void ToString_OfTheKeySet_NeverPrintsTheKeys()
    {
        var keySet = new KeySet(2, new byte[] { 11, 22, 33 }, new byte[] { 44, 55, 66 });

        var text = keySet.ToString();

        text.ShouldContain("Version = 2");
        text.ShouldNotContain("11");
        text.ShouldNotContain("ReadOnlyMemory");
    }

    [Fact]
    public void KeySet_MarksBothKeysAsSensitive()
    {
        typeof(KeySet).GetProperty(nameof(KeySet.EncryptionKey))!
            .GetCustomAttributes(typeof(SensitiveAttribute), true).ShouldNotBeEmpty();
        typeof(KeySet).GetProperty(nameof(KeySet.BlindIndexKey))!
            .GetCustomAttributes(typeof(SensitiveAttribute), true).ShouldNotBeEmpty();
    }
}
