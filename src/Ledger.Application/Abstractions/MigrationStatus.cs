namespace Ledger.Application.Abstractions;

public enum MigrationStatus
{
    Succeeded,
    ScriptFailed,
    ConnectionFailed,
    LockTimedOut
}
