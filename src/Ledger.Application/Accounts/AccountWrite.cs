using Ledger.Domain.Accounts;

namespace Ledger.Application.Accounts;

internal sealed record AccountWrite(AccountId AccountId, DateTimeOffset CreatedAt, bool AlreadyCreated);
