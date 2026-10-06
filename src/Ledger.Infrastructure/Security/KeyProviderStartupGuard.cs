using System.Security.Cryptography;
using Ledger.Application.Abstractions;
using Ledger.Application.Security;
using Ledger.Domain.Accounts;
using Ledger.Infrastructure.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Security;

internal sealed class KeyProviderStartupGuard(
    IKeyProvider keyProvider,
    IHolderDocumentProtector protector,
    IOptions<PiiOptions> options,
    IHostEnvironment environment,
    ILogger<KeyProviderStartupGuard> logger) : IHostedService
{
    private const string ProbeDocument = "52998224725";
    private static readonly Guid ProbeAccount = new("0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33");

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (environment.RequiresProductionControls() && options.Value.Provider == PiiProvider.Configuration)
        {
            throw new InvalidOperationException(
                $"{PiiOptions.SectionName}:Provider must not be Configuration outside Development and Testing.");
        }

        if (!keyProvider.IsAvailable)
        {
            KeyProviderLog.UnavailableAtStartup(logger);

            return Task.CompletedTask;
        }

        if (!RoundTripSucceeds())
        {
            SecurityLog.KeyMaterialRejected(logger, KeyMaterialProblem.RoundTripFailed.ToReason(), "the process will not start");

            throw new InvalidOperationException(
                "The active key set could not encrypt and decrypt a probe holder document.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private bool RoundTripSucceeds()
    {
        var document = HolderDocument.From(ProbeDocument).Value;
        var accountId = AccountId.From(ProbeAccount).Value;

        try
        {
            var protectedDocument = protector.Protect(document, accountId);
            var restored = protector.Unprotect(protectedDocument.Encrypted, accountId);

            return restored.IsSuccess && restored.Value.Normalized == document.Normalized;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
