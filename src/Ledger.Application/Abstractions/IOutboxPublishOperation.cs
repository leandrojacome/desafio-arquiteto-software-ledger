using Ledger.Application.Outbox;

namespace Ledger.Application.Abstractions;

public interface IOutboxPublishOperation : IDisposable
{
    void Confirmed();

    void Failed(PublishFailureReason reason);
}
