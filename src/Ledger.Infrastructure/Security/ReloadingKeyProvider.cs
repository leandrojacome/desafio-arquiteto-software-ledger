using System.Diagnostics.CodeAnalysis;
using Ledger.Application.Abstractions;
using Ledger.Application.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Security;

internal sealed class ReloadingKeyProvider : IKeyProvider, IDisposable
{
    private readonly IKeySetSource _source;
    private readonly ushort _activeVersion;
    private readonly ISecurityTelemetry _telemetry;
    private readonly ILogger<ReloadingKeyProvider> _logger;
    private readonly Func<KeySet, bool> _roundTrip;
    private readonly Lock _reloadGate = new();
    private readonly ITimer _timer;
    private KeySnapshot? _snapshot;
    private ushort[] _vanished = [];

    public ReloadingKeyProvider(
        IKeySetSource source,
        IOptions<PiiOptions> options,
        TimeProvider timeProvider,
        ISecurityTelemetry telemetry,
        ILogger<ReloadingKeyProvider> logger)
        : this(source, options, timeProvider, telemetry, logger, KeyRoundTrip.Succeeds)
    {
    }

    internal ReloadingKeyProvider(
        IKeySetSource source,
        IOptions<PiiOptions> options,
        TimeProvider timeProvider,
        ISecurityTelemetry telemetry,
        ILogger<ReloadingKeyProvider> logger,
        Func<KeySet, bool> roundTrip)
    {
        _source = source;
        _activeVersion = (ushort)options.Value.ActiveKeyVersion;
        _telemetry = telemetry;
        _logger = logger;
        _roundTrip = roundTrip;

        _snapshot = LoadAtStartup();

        var period = TimeSpan.FromMinutes(options.Value.ReloadMinutes);
        _timer = timeProvider.CreateTimer(_ => Reload(), null, period, period);
    }

    public bool IsAvailable => Volatile.Read(ref _snapshot) is not null;

    public KeySet Active => Snapshot().Active;

    public IReadOnlyCollection<KeySet> Live => Volatile.Read(ref _snapshot)?.Live ?? [];

    public IReadOnlyCollection<ushort> VanishedVersions => Volatile.Read(ref _vanished);

    public KeySet Get(ushort version)
    {
        return Snapshot().TryGet(version, out var keys)
            ? keys
            : throw new KeyNotFoundException($"No key set with version {version} is loaded.");
    }

    public void Dispose()
    {
        _timer.Dispose();
    }

    private KeySnapshot Snapshot()
    {
        return Volatile.Read(ref _snapshot)
               ?? throw new KeyProviderUnavailableException("The key source could not be read.");
    }

    private KeySnapshot BuildVerified(IReadOnlyList<RawKeySet> raws)
    {
        var snapshot = KeySetReader.BuildSnapshot(raws, _activeVersion, PiiOptionsValidator.ActiveVersionKey);

        if (!_roundTrip(snapshot.Active))
        {
            throw new KeyMaterialRejectedException(
                KeyMaterialProblem.RoundTripFailed,
                "The active key set could not encrypt and decrypt a probe value.");
        }

        return snapshot;
    }

    private KeySnapshot? LoadAtStartup()
    {
        try
        {
            return BuildVerified(_source.Load());
        }
        catch (KeySourceUnavailableException)
        {
            return null;
        }
        catch (KeyMaterialRejectedException exception)
        {
            SecurityLog.KeyMaterialRejected(_logger, exception.Problem.ToReason(), "the process will not start");

            throw;
        }
    }

    private static void RejectAlteredVersions(KeySnapshot? previous, KeySnapshot snapshot)
    {
        if (previous is null)
        {
            return;
        }

        if (snapshot.VersionsWithOtherKeysThan(previous).Count > 0)
        {
            throw new KeyMaterialRejectedException(
                KeyMaterialProblem.VersionChanged,
                "A key version that was already loaded came back with different key material.");
        }
    }

    private void RegisterVanishedVersions(KeySnapshot? previous, KeySnapshot snapshot)
    {
        IReadOnlyList<ushort> newlyVanished = previous is null ? [] : snapshot.VersionsMissingFrom(previous);

        var stillVanished = _vanished
            .Concat(newlyVanished)
            .Where(version => !snapshot.TryGet(version, out _))
            .Distinct()
            .Order()
            .ToArray();

        Volatile.Write(ref _vanished, stillVanished);

        if (newlyVanished.Count > 0)
        {
            SecurityLog.KeyVersionsVanished(_logger, string.Join(',', newlyVanished));
        }
    }

    [SuppressMessage("Design", "CA1031",
        Justification = "A timer callback must never let an exception escape; the failure is logged and counted.")]
    private void Reload()
    {
        lock (_reloadGate)
        {
            try
            {
                var previous = Volatile.Read(ref _snapshot);
                var snapshot = BuildVerified(_source.Load());

                RejectAlteredVersions(previous, snapshot);

                Volatile.Write(ref _snapshot, snapshot);
                RegisterVanishedVersions(previous, snapshot);
                _telemetry.KeyReloaded(true);

                if (_logger.IsEnabled(LogLevel.Information))
                {
                    var versions = string.Join(',', snapshot.Live.Select(set => set.Version));
                    SecurityLog.KeySetReloaded(_logger, versions, snapshot.Active.Version);
                }
            }
            catch (KeyMaterialRejectedException exception)
            {
                _telemetry.KeyReloaded(false);
                SecurityLog.KeyMaterialRejected(_logger, exception.Problem.ToReason(), "the previous key set stays in use");
            }
            catch (Exception exception)
            {
                _telemetry.KeyReloaded(false);
                SecurityLog.KeyReloadFailed(_logger, (exception.InnerException ?? exception).GetType().Name);
            }
        }
    }
}
