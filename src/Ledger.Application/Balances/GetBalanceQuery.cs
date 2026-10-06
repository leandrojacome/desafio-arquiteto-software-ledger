using Ledger.Domain.Accounts;

namespace Ledger.Application.Balances;

public sealed record GetBalanceQuery(AccountId AccountId, DateTimeOffset? AsOf, string ClientId, string CorrelationId);
