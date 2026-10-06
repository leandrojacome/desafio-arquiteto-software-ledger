namespace Ledger.Application.Accounts;

public sealed record KeyUsage(IReadOnlyDictionary<int, long> AccountsByVersion)
{
    public long AccountsBelow(int version) =>
        AccountsByVersion.Where(pair => pair.Key < version).Sum(pair => pair.Value);

    public IReadOnlyList<int> VersionsAbove(int version) =>
        [.. AccountsByVersion.Where(pair => pair.Key > version && pair.Value > 0).Select(pair => pair.Key).Order()];

    public IReadOnlyList<int> VersionsOutside(IReadOnlySet<int> readable) =>
        [.. AccountsByVersion.Where(pair => pair.Value > 0 && !readable.Contains(pair.Key)).Select(pair => pair.Key).Order()];
}
