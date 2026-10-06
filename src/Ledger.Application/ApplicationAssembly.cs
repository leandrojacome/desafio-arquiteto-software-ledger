using System.Reflection;

namespace Ledger.Application;

public static class ApplicationAssembly
{
    public static readonly Assembly Reference = typeof(ApplicationAssembly).Assembly;
}
