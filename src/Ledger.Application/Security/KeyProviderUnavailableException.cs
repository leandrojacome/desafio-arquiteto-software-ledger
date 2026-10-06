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
