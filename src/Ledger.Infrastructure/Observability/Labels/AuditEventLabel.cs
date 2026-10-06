using Ledger.Application.Audit;

namespace Ledger.Infrastructure.Observability.Labels;

internal enum AuditEventLabel
{
    [Label(AuditEventTypes.AccountCreated)]
    AccountCreated = 0,

    [Label(AuditEventTypes.AuthorizationDeniedWrite)]
    AuthorizationDeniedWrite = 1,

    [Label(AuditEventTypes.PiiDecrypted)]
    PiiDecrypted = 2,

    [Label(AuditEventTypes.PiiRewrapped)]
    PiiRewrapped = 3,

    [Label(AuditEventTypes.KeysVersionActivated)]
    KeysVersionActivated = 4
}
