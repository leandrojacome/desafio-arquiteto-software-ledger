using Ledger.Application.Abstractions;

namespace Ledger.Infrastructure.Persistence;

internal sealed class Uuid7IdGenerator(TimeProvider timeProvider) : IIdGenerator
{
    public Guid NewId() => Guid.CreateVersion7(timeProvider.GetUtcNow());
}
