using Ledger.Infrastructure.Persistence;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Reads;

internal sealed class ReadPoolHolder : IAsyncDisposable
{
    private readonly List<NpgsqlConnection> _held;

    private ReadPoolHolder(List<NpgsqlConnection> held)
    {
        _held = held;
    }

    public int Held => _held.Count;

    public static async Task<ReadPoolHolder> HoldAsync(IPostgresConnectionFactory connections, PostgresSource source, int size)
    {
        ArgumentNullException.ThrowIfNull(connections);

        var held = new List<NpgsqlConnection>();

        for (var index = 0; index < size; index++)
        {
            held.Add(await connections.OpenConnectionAsync(source, CancellationToken.None));
        }

        return new ReadPoolHolder(held);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _held)
        {
            await connection.DisposeAsync();
        }

        _held.Clear();
    }
}
