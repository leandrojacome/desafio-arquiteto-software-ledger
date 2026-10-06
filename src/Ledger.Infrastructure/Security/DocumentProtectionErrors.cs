using Ledger.Domain.Shared;

namespace Ledger.Infrastructure.Security;

internal static class DocumentProtectionErrors
{
    public static readonly Error Unreadable =
        new("HOLDER_DOCUMENT_UNREADABLE", "The protected holder document could not be read.", ErrorKind.Unprocessable);
}
