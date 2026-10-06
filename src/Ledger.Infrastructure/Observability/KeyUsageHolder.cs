namespace Ledger.Infrastructure.Observability;

internal sealed class KeyUsageHolder
{
    private Reading? _reading;

    public long? AccountsBelowActive => Volatile.Read(ref _reading)?.AccountsBelowActive;

    public bool Anomalous => Volatile.Read(ref _reading)?.Anomalous ?? false;

    public void Record(long accountsBelowActive, bool anomalous)
    {
        Volatile.Write(ref _reading, new Reading(accountsBelowActive, anomalous));
    }

    private sealed record Reading(long AccountsBelowActive, bool Anomalous);
}
