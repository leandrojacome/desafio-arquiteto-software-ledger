using Ledger.Application.Security;
using Ledger.Domain.Accounts;
using Ledger.Domain.Shared;

namespace Ledger.Application.Abstractions;

public interface IHolderDocumentProtector
{
    ProtectedHolderDocument Protect(HolderDocument document, AccountId accountId);

    Result<HolderDocument> Unprotect(ReadOnlyMemory<byte> encrypted, AccountId accountId);

    Result<ProtectedHolderDocument> Reprotect(ReadOnlyMemory<byte> encrypted, AccountId accountId);

    IReadOnlyList<ReadOnlyMemory<byte>> BlindIndexCandidates(HolderDocument document);
}
