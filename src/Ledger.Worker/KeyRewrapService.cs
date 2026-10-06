using Ledger.Application.Abstractions;
using Ledger.Application.Accounts;
using Ledger.Application.Outbox;
using Ledger.Application.Security;

namespace Ledger.Worker;

internal sealed class KeyRewrapService(
    WorkerLoopHost host,
    RewrapSettings settings,
    ILogger<KeyRewrapService> logger)
    : WorkerLoopService(WorkerLoop.KeyRewrap, host, logger)
{
    private bool _activationRecorded;

    protected override async Task<TimeSpan> CycleAsync(IServiceProvider services, CancellationToken stoppingToken)
    {
        if (!_activationRecorded)
        {
            await services.GetRequiredService<RecordKeyActivationHandler>().HandleAsync(stoppingToken);
            _activationRecorded = true;
        }

        var activeVersion = services.GetRequiredService<IKeyProvider>().Active.Version;
        var passId = services.GetRequiredService<IIdGenerator>().NewId().ToString("N");
        var command = new RewrapAccountsCommand(activeVersion, settings.BatchSize, passId);

        await services.GetRequiredService<RewrapAccountsHandler>().HandleAsync(command, stoppingToken);

        return settings.IdleInterval;
    }
}
