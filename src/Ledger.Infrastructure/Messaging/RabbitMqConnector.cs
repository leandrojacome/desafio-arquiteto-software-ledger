using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Ledger.Infrastructure.Messaging;

internal interface IRabbitMqConnector
{
    Task ConnectAsync(CancellationToken cancellationToken);
}

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

internal static class RabbitMqConnectionFactory
{
    public static ConnectionFactory Create(RabbitMqOptions options, string clientName)
    {
        var connectTimeout = TimeSpan.FromSeconds(options.ConnectTimeoutSeconds);

        return new ConnectionFactory
        {
            HostName = options.Host,
            Port = options.Port,
            VirtualHost = options.VirtualHost,
            UserName = options.Username,
            Password = options.Password,
            ClientProvidedName = clientName,
            AutomaticRecoveryEnabled = false,
            TopologyRecoveryEnabled = false,
            RequestedConnectionTimeout = connectTimeout,
            HandshakeContinuationTimeout = connectTimeout,
            ContinuationTimeout = connectTimeout,
            Ssl = new SslOption(options.Host, enabled: options.UseTls)
        };
    }
}
