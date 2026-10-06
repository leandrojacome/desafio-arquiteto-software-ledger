namespace Ledger.Application.Accounts;

public sealed record RewrapSettings(int BatchSize, TimeSpan IdleInterval);
