using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;

namespace Ledger.Application.Entries;

public sealed record ListEntriesQuery(
    AccountId AccountId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Limit,
    StatementPosition? Cursor,
    string ClientId,
    string CorrelationId);
