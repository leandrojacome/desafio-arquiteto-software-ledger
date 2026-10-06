using Ledger.Application.Accounts;
using Microsoft.Extensions.Logging;

namespace Ledger.Application.Security;

public static class KeyProviderLog
{
    public const string StartupOperation = "startup";

    public static void UnavailableAtStartup(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        AccountLog.KeyProviderUnavailable(logger, StartupOperation);
    }
}
