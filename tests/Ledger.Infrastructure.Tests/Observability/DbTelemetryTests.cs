using Ledger.Infrastructure.Observability.Labels;
using Ledger.Infrastructure.Tests.Observability.Support;

namespace Ledger.Infrastructure.Tests.Observability;

[Trait("Category", "Unit")]
public sealed class DbTelemetryTests : IDisposable
{
    private readonly TestTelemetry _telemetry = new();

    public void Dispose()
    {
        _telemetry.Dispose();
    }

    [Theory]
    [InlineData("insert_entry")]
    [InlineData("update_balance")]
    [InlineData("insert_outbox")]
    [InlineData("insert_idempotency_key")]
    [InlineData("select_balance")]
    [InlineData("select_balance_as_of")]
    [InlineData("select_entries")]
    public void CommandCompleted_ObservesTheSecondsWithTheOperation(string label)
    {
        LabelTable<DbOperation>.TryParse(label, out var operation).ShouldBeTrue();

        _telemetry.Db.CommandCompleted(operation, TimeSpan.FromMilliseconds(3));

        var measurement = _telemetry.Capture.Of("ledger.db.command.duration").ShouldHaveSingleItem();
        measurement.Unit.ShouldBe("s");
        measurement.Value.ShouldBe(0.003, 0.00001);
        measurement.Tags.Keys.ShouldBe(["operation"]);
        measurement.Tag("operation").ShouldBe(label);
    }

    [Theory]
    [InlineData("deadlock")]
    [InlineData("serialization_failure")]
    [InlineData("transient_connection")]
    public void TransactionRetried_CountsOneRetryWithTheReason(string label)
    {
        LabelTable<DbRetryReason>.TryParse(label, out var reason).ShouldBeTrue();

        _telemetry.Db.TransactionRetried(reason);

        var measurement = _telemetry.Capture.Of("ledger.db.retries").ShouldHaveSingleItem();
        measurement.Value.ShouldBe(1);
        measurement.Unit.ShouldBe("{retry}");
        measurement.Tags.Keys.ShouldBe(["reason"]);
        measurement.Tag("reason").ShouldBe(label);
    }
}
