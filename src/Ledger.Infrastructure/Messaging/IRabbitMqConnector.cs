namespace Ledger.Infrastructure.Messaging;

internal interface IRabbitMqConnector
{
    Task ConnectAsync(CancellationToken cancellationToken);
}
