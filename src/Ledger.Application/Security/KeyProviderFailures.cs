using Ledger.Application.Accounts;
using Microsoft.Extensions.Logging;

namespace Ledger.Application.Security;

public sealed class KeyProviderUnavailableException : Exception
{
    public KeyProviderUnavailableException()
        : base("The key provider is unavailable.")
    {
    }

    public KeyProviderUnavailableException(string message)
        : base(message)
    {
    }

    public KeyProviderUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public static class KeyProviderLog
{
    public const string StartupOperation = "startup";

    public static void UnavailableAtStartup(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        AccountLog.KeyProviderUnavailable(logger, StartupOperation);
    }
}
