namespace Ledger.Application.Integrity;

public sealed class InspectAccountIntegrityHandler(IIntegritySessions sessions)
{
    public async Task<AccountIntegrityReport> HandleAsync(
        InspectAccountIntegrityCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using var session = await sessions.OpenAsync(cancellationToken);

        return await session.InspectAccountAsync(command.AccountId, cancellationToken);
    }
}
