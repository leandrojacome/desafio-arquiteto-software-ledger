using System.Diagnostics.CodeAnalysis;
using Ledger.Application.Abstractions;
using Ledger.Application.Integrity;
using Ledger.Infrastructure.Health;
using Ledger.Infrastructure.Persistence;
using Ledger.Infrastructure.Persistence.Integrity;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ledger.Api.IntegrationTests.Infrastructure;

internal sealed class IntegrityHarness : IAsyncDisposable
{
    private static readonly TimeSpan RecentInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Overlap = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan FullLookback = TimeSpan.FromHours(24);
    private static readonly TimeSpan ChainSlice = TimeSpan.FromMinutes(10);

    private IntegrityHarness(
        EmptyDatabase database,
        PostgresConnectionFactory factory,
        IIntegritySessions sessions)
    {
        Database = database;
        Factory = factory;
        Sessions = sessions;
        Ledger = new IntegrityLedger(database);
        Telemetry = new RecordingIntegrityTelemetry();
        Heartbeat = new WorkerHeartbeat(TimeProvider.System);
        HandlerLog = new LogCapture<RunIntegrityCheckHandler>();
        Handler = CreateHandler(sessions);
    }

    public EmptyDatabase Database { get; }

    public PostgresConnectionFactory Factory { get; }

    public IIntegritySessions Sessions { get; }

    public IntegrityLedger Ledger { get; }

    public RecordingIntegrityTelemetry Telemetry { get; }

    public WorkerHeartbeat Heartbeat { get; }

    public LogCapture<RunIntegrityCheckHandler> HandlerLog { get; }

    public RunIntegrityCheckHandler Handler { get; }

    [SuppressMessage("Reliability", "CA2000",
        Justification = "Ownership of the database and the factory passes to the harness, which disposes both.")]
    public static async Task<IntegrityHarness> CreateAsync(
        PostgresFixture postgres,
        Func<IIntegritySessions, IIntegritySessions>? decorate = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(postgres);

        var database = await postgres.CreateEmptyDatabaseAsync(cancellationToken);
        var factory = PostgresFixture.CreateConnectionFactory(database.Settings);

        try
        {
            (await database.MigrateAsync(cancellationToken)).Succeeded.ShouldBeTrue();

            IIntegritySessions sessions = new PostgresIntegritySessions(
                factory,
                TimeProvider.System,
                NullLogger<PostgresIntegritySession>.Instance);

            return new IntegrityHarness(database, factory, decorate?.Invoke(sessions) ?? sessions);
        }
        catch
        {
            await factory.DisposeAsync();
            await database.DisposeAsync();

            throw;
        }
    }

    public RunIntegrityCheckHandler CreateHandler(IIntegritySessions sessions) =>
        new(
            sessions,
            Telemetry,
            Heartbeat,
            new GuidIdGenerator(),
            TimeProvider.System,
            HandlerLog);

    public async Task<IntegrityRunSummary> RunAsync(IntegrityMode mode, IIntegritySessions? sessions = null)
    {
        var handler = sessions is null ? Handler : CreateHandler(sessions);
        var result = await handler.HandleAsync(CommandFor(mode), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();

        return result.Value;
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        await Database.DisposeAsync();
    }

    public static RunIntegrityCheckCommand CommandFor(IntegrityMode mode) =>
        new(mode, RecentInterval, Overlap, FullLookback, ChainSlice, 5000);

    private sealed class GuidIdGenerator : IIdGenerator
    {
        public Guid NewId() => Guid.CreateVersion7();
    }
}
