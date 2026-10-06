using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;

namespace Ledger.Api.IntegrationTests.Infrastructure;

[SuppressMessage("Reliability", "CA2000", Justification = "Listeners and connections are owned and disposed by Pause.")]
[SuppressMessage("Usage", "CA2213", Justification = "Pause disposes the listener and the connections it detaches.")]
internal sealed class TcpProxy : IAsyncDisposable
{
    private readonly string _targetHost;
    private readonly int _targetPort;
    private readonly Lock _gate = new();
    private readonly List<ProxyConnection> _connections = [];
    private TcpListener? _listener;
    private CancellationTokenSource? _acceptance;
    private Task _acceptLoop = Task.CompletedTask;

    private TcpProxy(string targetHost, int targetPort, int port)
    {
        _targetHost = targetHost;
        _targetPort = targetPort;
        Port = port;
    }

    public int Port { get; }

    public static TcpProxy Start(string targetHost, int targetPort)
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);

        probe.Start();

        var port = ((IPEndPoint)probe.LocalEndpoint).Port;

        probe.Stop();
        probe.Dispose();

        var proxy = new TcpProxy(targetHost, targetPort, port);

        proxy.Resume();

        return proxy;
    }

    public void Resume()
    {
        lock (_gate)
        {
            if (_listener is not null)
            {
                return;
            }

            _listener = new TcpListener(IPAddress.Loopback, Port);
            _acceptance = new CancellationTokenSource();
            _listener.Start();
            _acceptLoop = AcceptAsync(_listener, _acceptance.Token);
        }
    }

    public void Pause()
    {
        TcpListener? listener;
        CancellationTokenSource? acceptance;
        List<ProxyConnection> open;

        lock (_gate)
        {
            listener = _listener;
            acceptance = _acceptance;
            _listener = null;
            _acceptance = null;
            open = [.. _connections];
            _connections.Clear();
        }

        acceptance?.Cancel();
        listener?.Stop();
        listener?.Dispose();

        foreach (var connection in open)
        {
            connection.Dispose();
        }

        acceptance?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        Pause();

        await _acceptLoop;
    }

    private async Task AcceptAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;

            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException
                                                  or SocketException)
            {
                return;
            }

            var connection = new ProxyConnection(client, _targetHost, _targetPort);

            lock (_gate)
            {
                _connections.Add(connection);
            }

            connection.Start();
        }
    }

    private sealed class ProxyConnection(TcpClient client, string targetHost, int targetPort) : IDisposable
    {
        private readonly TcpClient _upstream = new();
        private readonly CancellationTokenSource _cancellation = new();

        public void Start() => _ = PumpAsync();

        public void Dispose()
        {
            _cancellation.Cancel();
            client.Dispose();
            _upstream.Dispose();
            _cancellation.Dispose();
        }

        private async Task PumpAsync()
        {
            try
            {
                await _upstream.ConnectAsync(targetHost, targetPort, _cancellation.Token);

                var forward = client.GetStream().CopyToAsync(_upstream.GetStream(), _cancellation.Token);
                var backward = _upstream.GetStream().CopyToAsync(client.GetStream(), _cancellation.Token);

                await Task.WhenAny(forward, backward);
            }
            catch (Exception exception) when (exception is OperationCanceledException or IOException
                                                  or SocketException or ObjectDisposedException
                                                  or InvalidOperationException)
            {
                return;
            }
        }
    }
}
