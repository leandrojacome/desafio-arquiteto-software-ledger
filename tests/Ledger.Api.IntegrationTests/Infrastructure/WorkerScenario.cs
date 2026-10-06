using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Api.IntegrationTests.Infrastructure;

internal sealed class WorkerScenario : IAsyncDisposable
{
    private readonly PostgresFixture _postgres;
    private readonly RabbitMqFixture? _broker;
    private readonly List<MessagingWorkerFactory> _workers = [];

    private WorkerScenario(PostgresFixture postgres, RabbitMqFixture? broker, EmptyDatabase database)
    {
        _postgres = postgres;
        _broker = broker;
        Database = database;
        Seeder = new OutboxSeeder(database);
    }

    public EmptyDatabase Database { get; }

    public string RetentionQueue { get; } = $"test.retention.{Guid.NewGuid():N}";

    public string Exchange { get; private set; } = RabbitMqFixture.Exchange;

    public RetentionQueueReader Retention =>
        new(
            _broker ?? throw new InvalidOperationException("The scenario was created without a broker."),
            RetentionQueue,
            Exchange);

    public OutboxSeeder Seeder { get; }

    public QueueProbe? Probe { get; private set; }

    public QueueProbe RequiredProbe =>
        Probe ?? throw new InvalidOperationException("The scenario was created without a queue probe.");

    [SuppressMessage("Reliability", "CA2000",
        Justification = "The empty database is owned by the scenario, which drops it on dispose.")]
    public static async Task<WorkerScenario> CreateAsync(
        PostgresFixture postgres,
        RabbitMqFixture? broker,
        bool withProbe,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(postgres);

        var database = await postgres.CreateEmptyDatabaseAsync(cancellationToken);
        var scenario = new WorkerScenario(postgres, broker, database);

        try
        {
            var report = await database.MigrateAsync(cancellationToken);

            report.Succeeded.ShouldBeTrue();

            if (withProbe && broker is not null)
            {
                scenario.Probe = await QueueProbe.CreateAsync(broker, cancellationToken: cancellationToken);
            }

            return scenario;
        }
        catch
        {
            await scenario.DisposeAsync();

            throw;
        }
    }

    public Dictionary<string, string?> Settings(IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["RabbitMq:Retention:Queue"] = RetentionQueue,
            ["RabbitMq:Exchange"] = Exchange
        };

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                values[key] = value;
            }
        }

        return MessagingSettings.For(_postgres, Database, _broker, values);
    }

    public string UsePrivateExchange()
    {
        Exchange = $"test.events.{Guid.NewGuid():N}";

        return Exchange;
    }

    public MessagingWorkerFactory StartWorker(
        IReadOnlyDictionary<string, string?>? overrides = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var factory = new MessagingWorkerFactory(Settings(overrides), configureServices).Started();

        _workers.Add(factory);

        return factory;
    }

    public async Task StopWorkerAsync(MessagingWorkerFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        _workers.Remove(factory);

        await factory.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var worker in _workers)
        {
            await worker.DisposeAsync();
        }

        _workers.Clear();

        if (Probe is not null)
        {
            await Probe.DisposeAsync();
        }

        if (_broker is not null && !_broker.IsStopped)
        {
            await Retention.DeleteQuietlyAsync();
        }

        await Database.DisposeAsync();
    }
}
