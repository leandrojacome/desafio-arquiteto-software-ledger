using System.Text;
using Ledger.Application.Security;
using Ledger.Domain.Accounts;

namespace Ledger.Application.Accounts;

public sealed record RewrapAccountsCommand(int ActiveVersion, int BatchSize, string PassId);

public sealed record RewrapPassResult(int Rewrapped, int Failed, bool Converged, bool Skipped = false)
{
    public static RewrapPassResult SkippedPass { get; } = new(0, 0, false, true);
}

public sealed record RewrapSettings(int BatchSize, TimeSpan IdleInterval);

public sealed record RewrapBatchRequest(int ActiveVersion, AccountId? AfterId, int BatchSize, string PassId);

public sealed record RewrapBatchResult(int Selected, int Rewrapped, int Failed, AccountId? LastId);

public sealed record RewrapRow(AccountId Id, [property: Sensitive] ReadOnlyMemory<byte> Encrypted, int KeyVersion)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Id = ").Append(Id).Append(", KeyVersion = ").Append(KeyVersion);

        return true;
    }
}

public sealed record KeyUsage(IReadOnlyDictionary<int, long> AccountsByVersion)
{
    public long AccountsBelow(int version) =>
        AccountsByVersion.Where(pair => pair.Key < version).Sum(pair => pair.Value);

    public IReadOnlyList<int> VersionsAbove(int version) =>
        [.. AccountsByVersion.Where(pair => pair.Key > version && pair.Value > 0).Select(pair => pair.Key).Order()];

    public IReadOnlyList<int> VersionsOutside(IReadOnlySet<int> readable) =>
        [.. AccountsByVersion.Where(pair => pair.Value > 0 && !readable.Contains(pair.Key)).Select(pair => pair.Key).Order()];
}
