using Ledger.Domain.Accounts;

namespace Ledger.Api.IntegrationTests.Security;

internal sealed record SeededAccount(AccountId Id, HolderDocument Document);
