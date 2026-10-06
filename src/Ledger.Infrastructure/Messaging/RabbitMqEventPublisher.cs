using System.Diagnostics.CodeAnalysis;
using System.Text;
using Ledger.Application.Abstractions;
using Ledger.Application.Outbox;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Ledger.Infrastructure.Messaging;

internal sealed class RabbitMqEventPublisher(
    BrokerConnection connection,
    IOptions<RabbitMqOptions> options,
    TimeProvider timeProvider) : IEventPublisher
{
    public const string ProbeRoutingKey = "ledger.probe";

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    private readonly string _exchange = options.Value.Exchange;

    public BrokerCircuitState Circuit => BrokerCircuitState.Closed;

    public bool IsConnected => connection.IsConnected;

    public int ClaimBudget(int configuredBatchSize) => connection.IsConnected ? configuredBatchSize : 0;

    public Task<bool> TryConnectAsync(CancellationToken cancellationToken) =>
        connection.TryConnectAsync(cancellationToken);

    [SuppressMessage("Design", "CA1031",
        Justification = "Whatever the broken channel throws means the probe was not confirmed.")]
    public async Task<bool> ProbeAsync(CancellationToken cancellationToken)
    {
        var channel = connection.CurrentChannel;

        if (channel is null)
        {
            return false;
        }

        using var deadline = new CancellationTokenSource(ProbeTimeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        try
        {
            await channel.BasicPublishAsync(
                _exchange,
                ProbeRoutingKey,
                mandatory: false,
                new BasicProperties { DeliveryMode = DeliveryModes.Transient },
                ReadOnlyMemory<byte>.Empty,
                linked.Token);

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await connection.DiscardChannelAsync(channel);

            return false;
        }
    }

    public async Task PublishAsync(OutboxEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var (properties, body) = Assemble(envelope);

        var channel = connection.CurrentChannel ??
                      throw new EventPublishException(PublishFailureReason.BrokerUnavailable, envelope.Id);

        try
        {
            await channel.BasicPublishAsync(
                _exchange,
                envelope.Type,
                mandatory: true,
                properties,
                body,
                cancellationToken);
        }
        catch (PublishReturnException exception)
        {
            connection.ReportUnroutable();

            throw new EventPublishException(PublishFailureReason.Unroutable, envelope.Id, exception);
        }
        catch (PublishException exception)
        {
            await connection.DiscardChannelAsync(channel);

            throw new EventPublishException(PublishFailureReason.Nack, envelope.Id, exception);
        }
        catch (OperationCanceledException)
        {
            await connection.DiscardChannelAsync(channel);

            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await connection.DiscardChannelAsync(channel);

            throw new EventPublishException(PublishFailureReason.BrokerUnavailable, envelope.Id, exception);
        }

        connection.ReportRouted();
    }

    private static (BasicProperties Properties, byte[] Body) Assemble(OutboxEnvelope envelope)
    {
        try
        {
            return (EventEnvelopeMapper.Properties(envelope), EventEnvelopeMapper.Body(envelope));
        }
        catch (Exception exception) when (exception is ArgumentException or EncoderFallbackException
                                              or OverflowException or FormatException)
        {
            throw new EventPublishException(PublishFailureReason.Serialization, envelope.Id, exception);
        }
    }
}
