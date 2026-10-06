using System.Buffers;
using System.Collections.Concurrent;

namespace Ledger.Infrastructure.Observability;

internal sealed class ClientLabels(int capacity)
{
    public const string Unknown = "unknown";
    public const string Overflow = "other";
    public const int DefaultCapacity = 128;
    public const int MaxLength = 64;

    private static readonly SearchValues<char> AllowedCharacters =
        SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._:@-");

    private readonly ConcurrentDictionary<string, string> _known = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public ClientLabels()
        : this(DefaultCapacity)
    {
    }

    public string Resolve(string? clientId)
    {
        if (string.IsNullOrEmpty(clientId) || clientId.Length > MaxLength
                                           || clientId.AsSpan().ContainsAnyExcept(AllowedCharacters)
                                           || SensitiveTextMasker.Apply(clientId) != clientId)
        {
            return Unknown;
        }

        if (_known.TryGetValue(clientId, out var known))
        {
            return known;
        }

        lock (_gate)
        {
            if (_known.TryGetValue(clientId, out known))
            {
                return known;
            }

            if (_known.Count >= capacity)
            {
                return Overflow;
            }

            _known[clientId] = clientId;

            return clientId;
        }
    }
}
