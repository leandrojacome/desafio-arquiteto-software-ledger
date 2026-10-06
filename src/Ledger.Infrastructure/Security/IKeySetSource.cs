namespace Ledger.Infrastructure.Security;

internal interface IKeySetSource
{
    IReadOnlyList<RawKeySet> Load();
}
