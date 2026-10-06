namespace Ledger.Application.Integrity;

public interface IIntegritySessions
{
    Task<IIntegritySession?> TryBeginRunAsync(IntegrityMode mode, CancellationToken cancellationToken);

    Task<IIntegritySession> OpenAsync(CancellationToken cancellationToken);
}
