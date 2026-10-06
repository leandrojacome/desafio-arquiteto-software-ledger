namespace Ledger.Application.Accounts;

public sealed record RewrapAccountsCommand(int ActiveVersion, int BatchSize, string PassId);
