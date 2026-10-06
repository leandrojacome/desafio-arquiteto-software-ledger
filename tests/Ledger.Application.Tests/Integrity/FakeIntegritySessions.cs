using Ledger.Application.Integrity;

namespace Ledger.Application.Tests.Integrity;

internal sealed class FakeIntegritySessions(FakeIntegritySession? session) : IIntegritySessions
{
    public List<IntegrityMode> Attempts { get; } = [];

    public Task<IIntegritySession?> TryBeginRunAsync(IntegrityMode mode, CancellationToken cancellationToken)
    {
        Attempts.Add(mode);

        return Task.FromResult<IIntegritySession?>(session);
    }

    public Task<IIntegritySession> OpenAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IIntegritySession>(session ?? new FakeIntegritySession());
}
