using Ledger.Domain.Entries;

namespace Ledger.Api.Validation;

internal sealed record StatementInput(DateTimeOffset? From, DateTimeOffset? To, int Limit, StatementPosition? Cursor);
