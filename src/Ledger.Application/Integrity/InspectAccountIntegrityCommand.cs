using Ledger.Domain.Accounts;

namespace Ledger.Application.Integrity;

public sealed record InspectAccountIntegrityCommand(AccountId AccountId);
