namespace Ledger.Infrastructure.Security;

internal sealed class KeySourceUnavailableException : InvalidOperationException
{
    public KeySourceUnavailableException()
        : base("The key source is unavailable.")
    {
    }

    public KeySourceUnavailableException(string message)
        : base(message)
    {
    }

    public KeySourceUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
