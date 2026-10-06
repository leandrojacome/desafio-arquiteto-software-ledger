extern alias LedgerWorker;

using System.Reflection;
using Ledger.Application;
using Ledger.Domain;

namespace Ledger.Architecture.Tests.Layers;

internal static class LayerAssemblies
{
    public const string ApplicationNamespace = "Ledger.Application";
    public const string InfrastructureNamespace = "Ledger.Infrastructure";
    public const string ApiNamespace = "Ledger.Api";
    public const string WorkerNamespace = "Ledger.Worker";
    public const string PersistenceNamespace = "Ledger.Infrastructure.Persistence";
    public const string EndpointsNamespace = "Ledger.Api.Endpoints";

    public static readonly Assembly Domain = DomainAssembly.Reference;
    public static readonly Assembly Application = ApplicationAssembly.Reference;
    public static readonly Assembly Infrastructure = typeof(Ledger.Infrastructure.DependencyInjection).Assembly;
    public static readonly Assembly Api = typeof(Program).Assembly;
    public static readonly Assembly Worker = typeof(LedgerWorker::Program).Assembly;

    public static IReadOnlyList<string> LedgerReferencesOf(Assembly assembly)
    {
        return assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .OfType<string>()
            .Where(name => name.StartsWith("Ledger.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    public static IReadOnlyList<string> ReferencedAssemblyNamesOf(Assembly assembly)
    {
        return assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToList();
    }
}
