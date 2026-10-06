namespace Ledger.Infrastructure.Security;

internal enum KeyMaterialProblem
{
    BadLength = 1,
    IdenticalKeys = 2,
    BadBase64 = 3,
    NoActiveSet = 4,
    RoundTripFailed = 5,
    DuplicateVersion = 6,
    VersionChanged = 7
}
