using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace Ledger.Api.IntegrationTests.Persistence;

[Trait("Category", "Unit")]
public sealed class LoggerUpgradeLogTests
{
    private const int ErrorEventId = 5005;
    private const int DebugEventId = 5007;

    [Fact]
    public void TheFirstFailure_IsLoggedAsOneErrorLineAndTheFollowingOnesGoToDebug()
    {
        var capture = new LogCapture<MigrationRunner>();
        var log = new LoggerUpgradeLog(capture);
        var reason = $"42501: permission denied for schema public{Environment.NewLine}{Environment.NewLine}POSITION: 14";

        log.LogError("Script block number: {0}; Message: {1}", -1, reason);
        log.LogError("Npgsql.PostgresException (0x80004005): {0}", reason);
        log.LogError(new InvalidOperationException("boom"), "Upgrade failed due to an unexpected exception:");

        var errors = capture.Events.Where(captured => captured.Level == LogLevel.Error).ToList();

        errors.ShouldHaveSingleItem();
        errors[0].Id.ShouldBe(ErrorEventId);
        errors[0].Message.ShouldBe("Migration failed: Script block number: -1; Message: 42501: permission denied for schema public");

        var details = capture.Events.Where(captured => captured.Level == LogLevel.Debug).ToList();

        details.Count.ShouldBe(2);
        details.ShouldAllBe(captured => captured.Id == DebugEventId);
        details[1].Message.ShouldContain("boom");
    }

    [Fact]
    public void AFailureThatIsAlreadySingleLine_IsLoggedWhole()
    {
        var capture = new LogCapture<MigrationRunner>();
        var log = new LoggerUpgradeLog(capture);

        log.LogError("The script {0} failed", "0001_initial_schema.sql");

        capture.Events.ShouldHaveSingleItem().Message.ShouldBe("Migration failed: The script 0001_initial_schema.sql failed");
    }
}
