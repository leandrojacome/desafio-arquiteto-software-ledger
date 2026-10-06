using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Ledger.Api.IntegrationTests.Infrastructure;

internal enum VersionDecision
{
    Ignore = 1,
    Apply = 2,
    ApplyWithGap = 3
}

internal readonly record struct VersionOutcome(VersionDecision Decision, long GapFrom, long GapTo);

internal static class EventVersionRule
{
    public static VersionOutcome Evaluate(long lastVersion, long eventVersion)
    {
        if (eventVersion <= lastVersion)
        {
            return new VersionOutcome(VersionDecision.Ignore, 0, 0);
        }

        return eventVersion == lastVersion + 1
            ? new VersionOutcome(VersionDecision.Apply, 0, 0)
            : new VersionOutcome(VersionDecision.ApplyWithGap, lastVersion + 1, eventVersion - 1);
    }
}

internal sealed record ConsumedEvent(Guid MessageId, Guid AccountId, long AccountVersion, decimal BalanceAfter);

internal sealed class ReferenceConsumer : IAsyncDisposable
{
    public const string DefaultQueue = "reference-consumer.ledger.entry-registered";
    public const int SupportedSchemaVersion = 1;

    private const string SchemaSql = """
        CREATE SCHEMA IF NOT EXISTS consumer_ref;
        CREATE TABLE IF NOT EXISTS consumer_ref.processed_events (
            message_id uuid PRIMARY KEY,
            processed_at timestamptz NOT NULL
        );
        CREATE TABLE IF NOT EXISTS consumer_ref.account_state (
            account_id uuid PRIMARY KEY,
            last_version bigint NOT NULL,
            balance_after numeric(18,2) NOT NULL
        );
        CREATE TABLE IF NOT EXISTS consumer_ref.version_gaps (
            account_id uuid NOT NULL,
            missing_version bigint NOT NULL,
            detected_at timestamptz NOT NULL,
            PRIMARY KEY (account_id, missing_version)
        );
        """;

    private readonly RabbitMqFixture _broker;
    private readonly EmptyDatabase _database;
    private readonly IConnection _connection;
    private readonly IChannel _channel;
    private readonly Func<ConsumedEvent, bool>? _crashBeforeAck;
    private int _applied;
    private int _duplicates;
    private int _ignored;
    private int _rejected;
    private int _crashed;
    private Task? _abort;

    private ReferenceConsumer(
        RabbitMqFixture broker,
        EmptyDatabase database,
        IConnection connection,
        IChannel channel,
        string queue,
        Func<ConsumedEvent, bool>? crashBeforeAck)
    {
        _broker = broker;
        _database = database;
        _connection = connection;
        _channel = channel;
        _crashBeforeAck = crashBeforeAck;
        Queue = queue;
        DeadLetterQueue = DeadLetterQueueOf(queue);
    }

    public string Queue { get; }

    public string DeadLetterQueue { get; }

    public int Applied => Volatile.Read(ref _applied);

    public int Duplicates => Volatile.Read(ref _duplicates);

    public int Ignored => Volatile.Read(ref _ignored);

    public int Rejected => Volatile.Read(ref _rejected);

    public bool Crashed => Volatile.Read(ref _crashed) == 1;

    public static string DeadLetterQueueOf(string queue) => queue + ".dead-letter";

    public static string DeadLetterExchangeOf(string queue) => queue + ".dlx";

    public static async Task<ReferenceConsumer> StartAsync(
        RabbitMqFixture broker,
        EmptyDatabase database,
        string? queue = null,
        Func<ConsumedEvent, bool>? crashBeforeAck = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(broker);
        ArgumentNullException.ThrowIfNull(database);

        var queueName = queue ?? DefaultQueue;

        await EnsureSchemaAsync(database, cancellationToken);

        var connection = await broker.CreateConnectionAsync(cancellationToken);
        var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await DeclareTopologyAsync(channel, queueName, cancellationToken);
        await channel.BasicQosAsync(0, 50, false, cancellationToken);

        var consumer = new ReferenceConsumer(broker, database, connection, channel, queueName, crashBeforeAck);
        var eventing = new AsyncEventingBasicConsumer(channel);

        eventing.ReceivedAsync += consumer.OnReceivedAsync;

        await channel.BasicConsumeAsync(queueName, autoAck: false, eventing, cancellationToken);

        return consumer;
    }

    public async Task<long> ProcessedCountAsync(CancellationToken cancellationToken = default) =>
        await ScalarAsync("SELECT count(*) FROM consumer_ref.processed_events", cancellationToken);

    public async Task<long> GapCountAsync(CancellationToken cancellationToken = default) =>
        await ScalarAsync("SELECT count(*) FROM consumer_ref.version_gaps", cancellationToken);

    public async Task<IReadOnlyList<long>> GapsOfAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAdministrativeConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT missing_version FROM consumer_ref.version_gaps WHERE account_id = @id ORDER BY missing_version",
            connection);

        command.Parameters.Add("id", NpgsqlDbType.Uuid).Value = accountId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var gaps = new List<long>();

        while (await reader.ReadAsync(cancellationToken))
        {
            gaps.Add(reader.GetInt64(0));
        }

        return gaps;
    }

    public async Task<(long Version, decimal Balance)?> StateOfAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAdministrativeConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT last_version, balance_after FROM consumer_ref.account_state WHERE account_id = @id",
            connection);

        command.Parameters.Add("id", NpgsqlDbType.Uuid).Value = accountId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken) ? (reader.GetInt64(0), reader.GetDecimal(1)) : null;
    }

    public async Task SeedStateAsync(
        Guid accountId,
        long version,
        decimal balance,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAdministrativeConnectionAsync(cancellationToken);

        await SaveStateAsync(connection, null, new ConsumedEvent(Guid.NewGuid(), accountId, version, balance));
    }

    public Task<uint> DeadLetterCountAsync(CancellationToken cancellationToken = default) =>
        CountAsync(DeadLetterQueue, cancellationToken);

    public Task<uint> ReadyCountAsync(CancellationToken cancellationToken = default) =>
        CountAsync(Queue, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_abort is not null)
            {
                await _abort;
            }

            if (_connection.IsOpen)
            {
                await _channel.QueueDeleteAsync(Queue, ifUnused: false, ifEmpty: false, noWait: false, CancellationToken.None);
                await _channel.QueueDeleteAsync(DeadLetterQueue, ifUnused: false, ifEmpty: false, noWait: false, CancellationToken.None);
            }

            await _channel.DisposeAsync();
            await _connection.DisposeAsync();
        }
        catch (Exception exception) when (exception is IOException or RabbitMQ.Client.Exceptions.AlreadyClosedException
                                              or RabbitMQ.Client.Exceptions.OperationInterruptedException
                                              or ObjectDisposedException)
        {
            return;
        }
    }

    private async Task<uint> CountAsync(string queue, CancellationToken cancellationToken)
    {
        await using var connection = await _broker.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        return (await channel.QueueDeclarePassiveAsync(queue, cancellationToken)).MessageCount;
    }

    private static async Task DeclareTopologyAsync(IChannel channel, string queue, CancellationToken cancellationToken)
    {
        var deadLetterExchange = DeadLetterExchangeOf(queue);
        var deadLetterQueue = DeadLetterQueueOf(queue);

        await channel.ExchangeDeclareAsync(
            RabbitMqFixture.Exchange,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            noWait: false,
            cancellationToken);
        await channel.ExchangeDeclareAsync(
            deadLetterExchange,
            ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            arguments: null,
            noWait: false,
            cancellationToken);
        await channel.QueueDeclareAsync(
            deadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            noWait: false,
            cancellationToken);
        await channel.QueueBindAsync(deadLetterQueue, deadLetterExchange, "dead", null, false, cancellationToken);
        await channel.QueueDeclareAsync(
            queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",
                ["x-dead-letter-exchange"] = deadLetterExchange,
                ["x-dead-letter-routing-key"] = "dead"
            },
            noWait: false,
            cancellationToken);
        await channel.QueueBindAsync(queue, RabbitMqFixture.Exchange, RabbitMqFixture.RoutingKey, null, false, cancellationToken);
    }

    private static async Task EnsureSchemaAsync(EmptyDatabase database, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAdministrativeConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(SchemaSql, connection);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static ConsumedEvent? Parse(BasicDeliverEventArgs delivery)
    {
        if (!Guid.TryParse(delivery.BasicProperties.MessageId, out var messageId))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(delivery.Body.Span));

            var root = document.RootElement;

            if (root.GetProperty("schemaVersion").GetInt32() != SupportedSchemaVersion)
            {
                return null;
            }

            return new ConsumedEvent(
                messageId,
                Guid.Parse(root.GetProperty("accountId").GetString() ?? string.Empty),
                root.GetProperty("accountVersion").GetInt64(),
                decimal.Parse(
                    root.GetProperty("balanceAfter").GetString() ?? string.Empty,
                    System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or FormatException
                                              or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private async Task OnReceivedAsync(object sender, BasicDeliverEventArgs delivery)
    {
        var consumed = Parse(delivery);

        if (consumed is null)
        {
            await _channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false);
            Interlocked.Increment(ref _rejected);

            return;
        }

        var outcome = await ApplyAsync(consumed);

        if (_crashBeforeAck?.Invoke(consumed) == true)
        {
            Interlocked.Exchange(ref _crashed, 1);

            _abort = Task.Run(() => _connection.AbortAsync());

            return;
        }

        await _channel.BasicAckAsync(delivery.DeliveryTag, multiple: false);

        switch (outcome)
        {
            case ApplyOutcome.Duplicate:
                Interlocked.Increment(ref _duplicates);
                break;
            case ApplyOutcome.Old:
                Interlocked.Increment(ref _ignored);
                break;
            default:
                Interlocked.Increment(ref _applied);
                break;
        }
    }

    private async Task<ApplyOutcome> ApplyAsync(ConsumedEvent consumed)
    {
        await using var connection = await _database.OpenAdministrativeConnectionAsync(CancellationToken.None);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken.None);

        await using (var register = new NpgsqlCommand(
                         """
                         INSERT INTO consumer_ref.processed_events (message_id, processed_at)
                         VALUES (@message_id, clock_timestamp())
                         ON CONFLICT (message_id) DO NOTHING
                         """,
                         connection,
                         transaction))
        {
            register.Parameters.Add("message_id", NpgsqlDbType.Uuid).Value = consumed.MessageId;

            if (await register.ExecuteNonQueryAsync(CancellationToken.None) == 0)
            {
                await transaction.RollbackAsync(CancellationToken.None);

                return ApplyOutcome.Duplicate;
            }
        }

        var last = await LastVersionAsync(connection, transaction, consumed.AccountId);
        var decision = EventVersionRule.Evaluate(last, consumed.AccountVersion);

        if (decision.Decision == VersionDecision.Ignore)
        {
            await transaction.CommitAsync(CancellationToken.None);

            return ApplyOutcome.Old;
        }

        await SaveStateAsync(connection, transaction, consumed);

        if (decision.Decision == VersionDecision.ApplyWithGap)
        {
            await RecordGapsAsync(connection, transaction, consumed.AccountId, decision);
        }

        await transaction.CommitAsync(CancellationToken.None);

        return ApplyOutcome.Applied;
    }

    private static async Task<long> LastVersionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid accountId)
    {
        await using var command = new NpgsqlCommand(
            "SELECT last_version FROM consumer_ref.account_state WHERE account_id = @id FOR UPDATE",
            connection,
            transaction);

        command.Parameters.Add("id", NpgsqlDbType.Uuid).Value = accountId;

        return await command.ExecuteScalarAsync(CancellationToken.None) is long version ? version : 0L;
    }

    private static async Task SaveStateAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, ConsumedEvent consumed)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO consumer_ref.account_state (account_id, last_version, balance_after)
            VALUES (@id, @version, @balance)
            ON CONFLICT (account_id) DO UPDATE
            SET last_version = EXCLUDED.last_version, balance_after = EXCLUDED.balance_after
            """,
            connection,
            transaction);

        command.Parameters.Add("id", NpgsqlDbType.Uuid).Value = consumed.AccountId;
        command.Parameters.Add("version", NpgsqlDbType.Bigint).Value = consumed.AccountVersion;
        command.Parameters.Add("balance", NpgsqlDbType.Numeric).Value = consumed.BalanceAfter;

        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task RecordGapsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid accountId,
        VersionOutcome decision)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO consumer_ref.version_gaps (account_id, missing_version, detected_at)
            SELECT @id, series, clock_timestamp() FROM generate_series(@from, @to) AS series
            ON CONFLICT DO NOTHING
            """,
            connection,
            transaction);

        command.Parameters.Add("id", NpgsqlDbType.Uuid).Value = accountId;
        command.Parameters.Add("from", NpgsqlDbType.Bigint).Value = decision.GapFrom;
        command.Parameters.Add("to", NpgsqlDbType.Bigint).Value = decision.GapTo;

        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    [SuppressMessage("Security", "CA2100", Justification = "Callers pass literal SQL.")]
    private async Task<long> ScalarAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenAdministrativeConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);

        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }

    private enum ApplyOutcome
    {
        Applied = 1,
        Duplicate = 2,
        Old = 3
    }
}
