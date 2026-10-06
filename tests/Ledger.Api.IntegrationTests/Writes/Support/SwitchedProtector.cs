using Ledger.Application.Abstractions;
using Ledger.Application.Security;
using Ledger.Domain.Accounts;
using Ledger.Domain.Shared;

namespace Ledger.Api.IntegrationTests.Writes.Support;

internal sealed class OutageSwitch
{
    public bool Available { get; set; } = true;
}

internal sealed class SwitchedProtector(IHolderDocumentProtector inner, OutageSwitch outage) : IHolderDocumentProtector
{
    public ProtectedHolderDocument Protect(HolderDocument document, AccountId accountId)
    {
        return outage.Available ? inner.Protect(document, accountId) : throw new KeyProviderUnavailableException();
    }

    public Result<HolderDocument> Unprotect(ReadOnlyMemory<byte> encrypted, AccountId accountId) =>
        inner.Unprotect(encrypted, accountId);

    public Result<ProtectedHolderDocument> Reprotect(ReadOnlyMemory<byte> encrypted, AccountId accountId) =>
        inner.Reprotect(encrypted, accountId);

    public IReadOnlyList<ReadOnlyMemory<byte>> BlindIndexCandidates(HolderDocument document) =>
        inner.BlindIndexCandidates(document);
}
