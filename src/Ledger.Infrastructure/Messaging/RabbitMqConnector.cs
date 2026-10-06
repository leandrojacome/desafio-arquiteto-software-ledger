using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Ledger.Infrastructure.Messaging;

internal sealed class RabbitMqConnector(IOptions<RabbitMqOptions> options) : IRabbitMqConnector
{
    private const string ClientName = "ledger-worker-probe";

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var factory = RabbitMqConnectionFactory.Create(options.Value, ClientName);

        await using var connection = await factory.CreateConnectionAsync(cancellationToken);

        await connection.CloseAsync(cancellationToken);
    }
}
