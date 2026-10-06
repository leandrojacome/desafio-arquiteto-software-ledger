using Ledger.Domain.Accounts;

namespace Ledger.Application.Accounts;

public sealed record RewrapBatchResult(int Selected, int Rewrapped, int Failed, AccountId? LastId);
