using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using Docker.DotNet;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Testcontainers.RabbitMq;

namespace Ledger.Api.IntegrationTests.Infrastructure;

[SuppressMessage("Design", "CA1001",
    Justification = "xUnit disposes collection fixtures through IAsyncLifetime.DisposeAsync.")]
public sealed class RabbitMqFixture : IAsyncLifetime
{
    public const string Username = "ledger_worker";
    public const string Password = "integration-tests-broker-password";
    public const string Exchange = "ledger.events";
    public const string RoutingKey = "EntryRegistered";

    private const string Image = "rabbitmq:3.13-management";
    private const int AmqpPort = 5672;
    private const int StartAttempts = 4;

    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(90);

    private RabbitMqContainer? _container;

    public string Host { get; } = "127.0.0.1";

    public int Port { get; private set; }

    public bool IsStopped { get; private set; }

    public async Task InitializeAsync()
    {
        if (DockerAvailability.SkipReason() is not null)
        {
            return;
        }

        if (!DockerAvailability.IsAvailable)
        {
            throw new InvalidOperationException(DockerAvailability.MissingReason);
        }

        for (var attempt = 1; ; attempt++)
        {
            Port = FreePort();

            _container = new RabbitMqBuilder(Image)
                .WithUsername(Username)
                .WithPassword(Password)
                .WithPortBinding(Port, AmqpPort)
                .Build();

            try
            {
                await _container.StartAsync();

                return;
            }
            catch (DockerApiException) when (attempt < StartAttempts)
            {
                await _container.DisposeAsync();
            }
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        IsStopped = true;

        return DockerCli.RunAsync($"stop {ContainerId}", cancellationToken);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await DockerCli.RunAsync($"start {ContainerId}", cancellationToken);
        await WaitUntilAcceptingConnectionsAsync(cancellationToken);

        IsStopped = false;
    }

    public Task PauseAsync(CancellationToken cancellationToken) =>
        DockerCli.RunAsync($"pause {ContainerId}", cancellationToken);

    public Task UnpauseAsync(CancellationToken cancellationToken) =>
        DockerCli.RunAsync($"unpause {ContainerId}", cancellationToken);

    public Task<IConnection> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = Host,
            Port = Port,
            UserName = Username,
            Password = Password,
            AutomaticRecoveryEnabled = false,
            RequestedConnectionTimeout = TimeSpan.FromSeconds(5)
        };

        return factory.CreateConnectionAsync(cancellationToken);
    }

    private string ContainerId =>
        _container?.Id ?? throw new InvalidOperationException("The RabbitMQ container is not available.");

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);

        listener.Start();

        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private async Task WaitUntilAcceptingConnectionsAsync(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ReadyTimeout);

        while (true)
        {
            try
            {
                await using var connection = await CreateConnectionAsync(deadline.Token);

                return;
            }
            catch (Exception exception) when (exception is BrokerUnreachableException or IOException
                                                  or SocketException or OperationInterruptedException
                                                  or AuthenticationFailureException or TimeoutException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), deadline.Token);
            }
        }
    }
}
