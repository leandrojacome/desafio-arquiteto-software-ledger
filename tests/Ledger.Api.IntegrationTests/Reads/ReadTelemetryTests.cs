using System.Net;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Observability;
using Ledger.Api.IntegrationTests.Persistence.Support;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;

namespace Ledger.Api.IntegrationTests.Reads;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class ReadTelemetryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string BalanceDuration = "ledger.balance.query.duration";
    private const string CommandDuration = "ledger.db.command.duration";

    private ReadWorld _world = null!;

    public Task InitializeAsync()
    {
        _world = ReadWorld.Create(postgres);

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _world.DisposeAsync();
    }

    [DockerFact]
    public async Task Balance_RecordsOneObservationPerQueryLabelledByMode()
    {
        var accountId = await _world.Host.CreateFundedAccountAsync(10.00m);

        using var durations = new MeterCapture(BalanceDuration);

        using var current = await _world.Client.BalanceAsync(accountId);
        using var historical = await _world.Client.BalanceAtAsync(accountId, ReadClock.UtcNow.AddHours(-1));
        using var invalid = await _world.Client.BalanceAsOfAsync(accountId, "abc");

        current.Status.ShouldBe(HttpStatusCode.OK);
        historical.Status.ShouldBe(HttpStatusCode.OK);
        invalid.Status.ShouldBe(HttpStatusCode.BadRequest);
        durations.Measurements.Select(measurement => (string)(measurement.Tags["mode"] ?? string.Empty))
            .ShouldBe(["current", "as_of"]);
        durations.Measurements.ShouldAllBe(measurement => measurement.Tags.Count == 1 && measurement.Value >= 0);
    }

    [DockerFact]
    public async Task Reads_RecordOneCommandDurationPerSqlCommandWithTheOperationLabel()
    {
        var accountId = await _world.Host.CreateFundedAccountAsync(10.00m);

        using var commands = new MeterCapture(CommandDuration);

        using var current = await _world.Client.BalanceAsync(accountId);
        using var historical = await _world.Client.BalanceAtAsync(accountId, ReadClock.UtcNow.AddHours(-1));
        using var statement = await _world.Client.StatementAsync(accountId);

        current.Status.ShouldBe(HttpStatusCode.OK);
        historical.Status.ShouldBe(HttpStatusCode.OK);
        statement.Status.ShouldBe(HttpStatusCode.OK);
        commands.Measurements.Select(measurement => (string)(measurement.Tags["operation"] ?? string.Empty))
            .ShouldBe(["select_balance", "select_balance_as_of", "select_entries"]);
        commands.Measurements.ShouldAllBe(measurement => measurement.Tags.Keys.All(key => key == "operation"));
    }

    [DockerFact]
    public async Task Reads_OpenSpansWithTheDocumentedAttributesAndNothingElse()
    {
        var accountId = await _world.Host.CreateAccountAsync();
        await _world.Ledger.SeedChainAsync(accountId, 3, ReadClock.UtcNow.AddHours(-1), TimeSpan.FromSeconds(1));

        using var spans = new ReadSpanCapture();

        using var current = await _world.Client.BalanceAsync(accountId);
        using var historical = await _world.Client.BalanceAtAsync(accountId, ReadClock.UtcNow.AddMinutes(-30));
        using var statement = await _world.Client.StatementAsync(accountId, "limit=2");

        current.Status.ShouldBe(HttpStatusCode.OK);
        historical.Status.ShouldBe(HttpStatusCode.OK);
        statement.Status.ShouldBe(HttpStatusCode.OK);

        var balanceSpans = spans.Spans.Where(span => span.Name == "ledger.balance_query").ToList();
        var statementSpan = spans.Spans.Single(span => span.Name == "ledger.statement_query");

        balanceSpans.Select(span => span.Tags["ledger.balance.mode"]).ShouldBe(["current", "as_of"]);
        balanceSpans.ShouldAllBe(span => span.Tags.Count == 1);
        statementSpan.Tags["ledger.statement.limit"].ShouldBe(2);
        statementSpan.Tags["ledger.statement.returned"].ShouldBe(2);
        statementSpan.Tags["ledger.statement.has_next"].ShouldBe(true);
        statementSpan.Tags.Count.ShouldBe(3);
    }

    [DockerFact]
    public async Task Reads_NeverPutAccountsAmountsOrCursorsInMetricLabelsOrSpanAttributes()
    {
        var accountId = await _world.Host.CreateFundedAccountAsync(4321.09m);
        await _world.Host.RegisterAsync(accountId, "second-entry", Ledger.Domain.Entries.EntryType.Credit, 1.00m);
        using var durations = new MeterCapture(BalanceDuration);
        using var commands = new MeterCapture(CommandDuration);
        using var spans = new ReadSpanCapture();

        using var current = await _world.Client.BalanceAsync(accountId);
        using var first = await _world.Client.StatementAsync(accountId, "limit=1");
        var cursor = first.Text("nextCursor");
        using var second = await _world.Client.StatementAsync(accountId, $"limit=1&cursor={Uri.EscapeDataString(cursor)}");

        var everything = string.Join(
            ' ',
            durations.Measurements.SelectMany(measurement => measurement.Tags.Values)
                .Concat(commands.Measurements.SelectMany(measurement => measurement.Tags.Values))
                .Concat(spans.Spans.SelectMany(span => span.Tags.Values))
                .Select(value => value?.ToString()));

        current.Status.ShouldBe(HttpStatusCode.OK);
        second.Status.ShouldBe(HttpStatusCode.OK);
        spans.Spans.Count.ShouldBeGreaterThan(0);
        everything.ShouldNotContain(accountId.ToString());
        everything.ShouldNotContain("4321.09");
        everything.ShouldNotContain(cursor);
    }

    [DockerFact]
    public async Task Histograms_OfTheReadPath_UseTheBoundariesOfTheObservabilityDocument()
    {
        using var clean = OtelEnvironment.Clean();
        using var exporter = new CapturingMetricExporter();
        await using var world = ReadWorld.Create(
            postgres,
            configureServices: services => services.ConfigureOpenTelemetryMeterProvider(
                metrics => metrics.AddReader(new PeriodicExportingMetricReader(exporter, int.MaxValue))));
        var accountId = await world.Host.CreateFundedAccountAsync(10.00m);

        using var balance = await world.Client.BalanceAsync(accountId);
        using var statement = await world.Client.StatementAsync(accountId);

        world.Factory.Services.GetRequiredService<MeterProvider>().ForceFlush(5000).ShouldBeTrue();

        var query = exporter.Metrics.Last(metric => metric.Name == BalanceDuration);
        var command = exporter.Metrics.Last(metric => metric.Name == CommandDuration);

        query.Unit.ShouldBe("s");
        query.Boundaries.ShouldBe([0.002, 0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.15, 0.25, 0.5, 1, 2.5]);
        command.Unit.ShouldBe("s");
        command.Boundaries.ShouldBe([0.001, 0.002, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1]);
    }
}
