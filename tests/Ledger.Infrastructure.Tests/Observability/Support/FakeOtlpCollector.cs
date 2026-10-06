using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Ledger.Infrastructure.Tests.Observability.Support;

internal sealed class FakeOtlpCollector : IDisposable
{
    private static readonly byte[] EmptyOk =
        Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentQueue<string> _requestLines = new();

    public FakeOtlpCollector()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(() => AcceptLoopAsync(_stop.Token));
    }

    public int Port { get; }

    public string Endpoint => $"http://127.0.0.1:{Port}";

    public IReadOnlyList<string> RequestLines => [.. _requestLines];

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Dispose();
        _stop.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                _ = Task.Run(() => HandleAsync(client, cancellationToken), cancellationToken);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var buffer = new byte[8192];
                var read = await stream.ReadAsync(buffer, cancellationToken);
                var text = Encoding.ASCII.GetString(buffer, 0, read);
                var lineEnd = text.IndexOf("\r\n", StringComparison.Ordinal);

                _requestLines.Enqueue(lineEnd < 0 ? text : text[..lineEnd]);
                await stream.WriteAsync(EmptyOk, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException)
            {
            }
        }
    }
}
