using System.Reflection;

namespace Ledger.Infrastructure;

internal static class InfrastructureAssembly
{
    public static readonly Assembly Reference = typeof(InfrastructureAssembly).Assembly;
}
