using System.Reflection;

namespace Ledger.Domain;

public static class DomainAssembly
{
    public static readonly Assembly Reference = typeof(DomainAssembly).Assembly;
}
