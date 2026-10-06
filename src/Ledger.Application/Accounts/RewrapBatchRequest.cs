using Ledger.Domain.Accounts;

namespace Ledger.Application.Accounts;

public sealed record RewrapBatchRequest(int ActiveVersion, AccountId? AfterId, int BatchSize, string PassId);
