namespace Ledger.Api.Security;

internal enum PublicKeyProblem
{
    None = 0,
    Unreadable = 1,
    ContainsPrivateKey = 2,
    NotAPublicKey = 3,
    WeakKey = 4
}
