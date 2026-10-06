using Ledger.Application.Abstractions;

namespace Ledger.Infrastructure.Persistence;

internal sealed record MigrationReport(MigrationStatus Status, IReadOnlyList<string> AppliedScripts, Exception? Error)
{
    public bool Succeeded => Status == MigrationStatus.Succeeded;
}
