using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Ledger.Api.IntegrationTests.Observability;

internal sealed class FakeOtlpCollector : IDisposable
{
    private const string ContentLengthHeader = "Content-Length";
    private const string LineBreak = "\r\n";
    private const string HeaderTerminator = LineBreak + LineBreak;

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
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException
                                                  or SocketException)
            {
                return;
            }
        }
    }

    private static int ContentLengthOf(string headers)
    {
        foreach (var line in headers.Split(LineBreak))
        {
            var separator = line.IndexOf(':', StringComparison.Ordinal);

            if (separator > 0
                && line[..separator].Equals(ContentLengthHeader, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line[(separator + 1)..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var length))
            {
                return length;
            }
        }

        return 0;
    }

    private async Task HandleAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var buffer = new byte[8192];
                using var received = new MemoryStream();
                var requestLine = string.Empty;
                var headerLength = -1;
                var contentLength = 0;

                while (headerLength < 0 || received.Length < headerLength + contentLength)
                {
                    var read = await stream.ReadAsync(buffer, cancellationToken);

                    if (read == 0)
                    {
                        break;
                    }

                    await received.WriteAsync(buffer.AsMemory(0, read), cancellationToken);

                    if (headerLength >= 0)
                    {
                        continue;
                    }

                    var text = Encoding.ASCII.GetString(received.GetBuffer(), 0, (int)received.Length);
                    var headerEnd = text.IndexOf(HeaderTerminator, StringComparison.Ordinal);

                    if (headerEnd < 0)
                    {
                        continue;
                    }

                    headerLength = headerEnd + HeaderTerminator.Length;
                    contentLength = ContentLengthOf(text[..headerEnd]);
                    requestLine = text[..text.IndexOf(LineBreak, StringComparison.Ordinal)];
                }

                _requestLines.Enqueue(requestLine);
                await stream.WriteAsync(EmptyOk, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException
                                                  or ObjectDisposedException)
            {
            }
        }
    }
}
