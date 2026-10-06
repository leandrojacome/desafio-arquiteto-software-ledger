using Ledger.Application.Abstractions;
using NSubstitute;

namespace Ledger.Application.Tests.Entries.Support;

internal sealed class RecordingScope : IUnitOfWorkScope
{
    public IAccountRepository Accounts { get; } = Substitute.For<IAccountRepository>();

    public IEntryRepository Entries { get; } = Substitute.For<IEntryRepository>();

    public IIdempotencyStore IdempotencyKeys { get; } = Substitute.For<IIdempotencyStore>();

    public IOutbox Outbox { get; } = Substitute.For<IOutbox>();

    public IAuditTrail Audit { get; } = Substitute.For<IAuditTrail>();

    public bool RollbackMarked { get; private set; }

    public void MarkForRollback()
    {
        RollbackMarked = true;
    }

    public void ClearMark()
    {
        RollbackMarked = false;
    }
}
