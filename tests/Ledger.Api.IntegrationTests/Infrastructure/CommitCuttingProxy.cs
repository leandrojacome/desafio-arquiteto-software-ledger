using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Ledger.Api.IntegrationTests.Infrastructure;

internal sealed class CommitCuttingProxy : IAsyncDisposable
{
    private const int SslRequestCode = 80_877_103;
    private const int GssEncryptionRequestCode = 80_877_104;
    private const byte SimpleQuery = (byte)'Q';
    private const int StartupHeaderLength = 4;
    private const int TypedHeaderLength = 5;
    private const int BufferSize = 16_384;

    private static readonly byte[] ReadyForQueryIdle = [(byte)'Z', 0, 0, 0, 5, (byte)'I'];
    private static readonly byte[] CommitText = Encoding.ASCII.GetBytes("COMMIT\0");

    private readonly TcpListener _listener;
    private readonly string _upstreamHost;
    private readonly int _upstreamPort;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentBag<Task> _connections = [];
    private readonly ConcurrentQueue<string> _commitMessageKinds = new();
    private readonly Task _accepting;

    private int _commitsToCut;
    private int _commitsCut;

    private CommitCuttingProxy(string upstreamHost, int upstreamPort)
    {
        _upstreamHost = upstreamHost;
        _upstreamPort = upstreamPort;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _accepting = AcceptAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public int CommitsCut => Volatile.Read(ref _commitsCut);

    public IReadOnlyList<string> CommitMessageKinds => [.. _commitMessageKinds];

    public static CommitCuttingProxy Start(string upstreamHost, int upstreamPort) => new(upstreamHost, upstreamPort);

    public void CutNextCommit(int count = 1) => Interlocked.Exchange(ref _commitsToCut, count);

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        _listener.Dispose();

        try
        {
            await _accepting;
            await Task.WhenAll(_connections);
        }
        catch (OperationCanceledException)
        {
            _commitMessageKinds.Enqueue("stopped");
        }

        _stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;

            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException
                                                  or SocketException)
            {
                return;
            }

            _connections.Add(RelayAsync(client));
        }
    }

    [SuppressMessage("Reliability", "CA2025",
        Justification = "Both pumps are awaited with Task.WhenAll before the sockets leave scope.")]
    private async Task RelayAsync(TcpClient client)
    {
        using var downstream = client;
        using var upstream = new TcpClient();
        var state = new ConnectionState();

        try
        {
            await upstream.ConnectAsync(_upstreamHost, _upstreamPort, _stop.Token);

            var clientStream = downstream.GetStream();
            var serverStream = upstream.GetStream();

            var toServer = GuardedAsync(() => ClientToServerAsync(clientStream, serverStream, state));
            var toClient = GuardedAsync(() => ServerToClientAsync(serverStream, clientStream, state));

            await Task.WhenAny(toServer, toClient);

            downstream.Close();
            upstream.Close();

            await Task.WhenAll(toServer, toClient);
        }
        catch (Exception exception) when (IsConnectionEnd(exception))
        {
            _commitMessageKinds.Enqueue("closed");
        }
    }

    private async Task GuardedAsync(Func<Task> pump)
    {
        try
        {
            await pump();
        }
        catch (Exception exception) when (IsConnectionEnd(exception))
        {
            _commitMessageKinds.Enqueue("closed");
        }
    }

    private static bool IsConnectionEnd(Exception exception) =>
        exception is IOException or SocketException or OperationCanceledException or ObjectDisposedException;

    private async Task ClientToServerAsync(
        NetworkStream client,
        NetworkStream server,
        ConnectionState state)
    {
        var startupDone = false;

        while (!startupDone)
        {
            var header = new byte[StartupHeaderLength];

            await client.ReadExactlyAsync(header, _stop.Token);

            var length = BinaryPrimitives.ReadInt32BigEndian(header);
            var body = new byte[length - StartupHeaderLength];

            await client.ReadExactlyAsync(body, _stop.Token);
            await server.WriteAsync(header, _stop.Token);
            await server.WriteAsync(body, _stop.Token);

            var code = BinaryPrimitives.ReadInt32BigEndian(body);

            startupDone = code != SslRequestCode && code != GssEncryptionRequestCode;
        }

        while (true)
        {
            var header = new byte[TypedHeaderLength];

            await client.ReadExactlyAsync(header, _stop.Token);

            var length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(1));
            var body = new byte[length - StartupHeaderLength];

            await client.ReadExactlyAsync(body, _stop.Token);

            var isCommit = body.AsSpan().SequenceEqual(CommitText);

            if (isCommit)
            {
                _commitMessageKinds.Enqueue(header[0] == SimpleQuery ? "simple-query" : $"message-{(char)header[0]}");
            }

            var mustCut = isCommit && header[0] == SimpleQuery && TryTakeCut();

            if (mustCut)
            {
                state.CommitSent = true;
            }

            await server.WriteAsync(header, _stop.Token);
            await server.WriteAsync(body, _stop.Token);

            if (mustCut)
            {
                await state.CommitAnswered.Task.WaitAsync(_stop.Token);
                Interlocked.Increment(ref _commitsCut);

                return;
            }
        }
    }

    private async Task ServerToClientAsync(NetworkStream server, NetworkStream client, ConnectionState state)
    {
        var buffer = new byte[BufferSize];
        var tail = new List<byte>();

        while (true)
        {
            var read = await server.ReadAsync(buffer, _stop.Token);

            if (read == 0)
            {
                return;
            }

            if (state.CommitSent)
            {
                tail.AddRange(buffer.AsSpan(0, read).ToArray());

                if (Contains(tail, ReadyForQueryIdle))
                {
                    state.CommitAnswered.TrySetResult();
                }

                continue;
            }

            await client.WriteAsync(buffer.AsMemory(0, read), _stop.Token);
        }
    }

    private bool TryTakeCut()
    {
        while (true)
        {
            var remaining = Volatile.Read(ref _commitsToCut);

            if (remaining <= 0)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _commitsToCut, remaining - 1, remaining) == remaining)
            {
                return true;
            }
        }
    }

    private static bool Contains(List<byte> haystack, byte[] needle)
    {
        return haystack.ToArray().AsSpan().IndexOf(needle) >= 0;
    }

    private sealed class ConnectionState
    {
        public volatile bool CommitSent;

        public TaskCompletionSource CommitAnswered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
