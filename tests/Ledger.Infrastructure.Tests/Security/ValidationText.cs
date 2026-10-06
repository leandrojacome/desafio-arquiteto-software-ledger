using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Tests.Security;

internal static class ValidationText
{
    public static string Message(this ValidateOptionsResult result)
    {
        return result.FailureMessage.ShouldNotBeNull();
    }
}
