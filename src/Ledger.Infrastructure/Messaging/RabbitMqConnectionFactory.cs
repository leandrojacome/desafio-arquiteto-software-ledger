using RabbitMQ.Client;

namespace Ledger.Infrastructure.Messaging;

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
