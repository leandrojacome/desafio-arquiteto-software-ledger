namespace Ledger.Application.Abstractions;

public interface IOutbox
{
    Task EnqueueAsync(OutboxMessage message, CancellationToken cancellationToken);
}
