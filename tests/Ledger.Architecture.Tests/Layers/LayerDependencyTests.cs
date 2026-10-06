using System.Reflection;
using System.Runtime.CompilerServices;
using NetArchTest.Rules;

namespace Ledger.Architecture.Tests.Layers;

[Trait("Category", "Architecture")]
public sealed class LayerDependencyTests
{
    private const BindingFlags AnyInstanceMember = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly string[] ForbiddenInDomain =
    [
        LayerAssemblies.ApplicationNamespace,
        LayerAssemblies.InfrastructureNamespace,
        LayerAssemblies.ApiNamespace,
        LayerAssemblies.WorkerNamespace,
        "Microsoft.Extensions",
        "Microsoft.AspNetCore",
        "System.Data",
        "Npgsql",
        "Dapper",
        "RabbitMQ",
        "Serilog",
        "OpenTelemetry"
    ];

    private static readonly string[] ForbiddenInApplication =
    [
        LayerAssemblies.InfrastructureNamespace,
        LayerAssemblies.ApiNamespace,
        LayerAssemblies.WorkerNamespace,
        "Microsoft.AspNetCore",
        "System.Data",
        "Npgsql",
        "Dapper",
        "RabbitMQ",
        "Serilog",
        "OpenTelemetry",
        "DbUp"
    ];

    private static readonly string[] UseCaseNamespaces =
    [
        "Ledger.Application.Accounts",
        "Ledger.Application.Entries",
        "Ledger.Application.Balances"
    ];

    [Fact]
    public void Domain_DependsOnNothingButTheBaseLibrary()
    {
        var result = Types.InAssembly(LayerAssemblies.Domain)
            .ShouldNot()
            .HaveDependencyOnAny(ForbiddenInDomain)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailingTypes(result));
    }

    [Fact]
    public void Domain_ReferencesNoAssemblyOutsideTheBaseLibrary()
    {
        var references = LayerAssemblies.ReferencedAssemblyNamesOf(LayerAssemblies.Domain);

        references.ShouldAllBe(name =>
            name == "System.Runtime" || name.StartsWith("System.", StringComparison.Ordinal) || name == "netstandard" ||
            name == "mscorlib");
    }

    [Fact]
    public void Application_ReferencesOnlyTheDomainAmongTheLedgerAssemblies()
    {
        LayerAssemblies.LedgerReferencesOf(LayerAssemblies.Application).ShouldBeSubsetOf(["Ledger.Domain"]);
    }

    [Fact]
    public void Application_DoesNotDependOnInfrastructureConcerns()
    {
        var result = Types.InAssembly(LayerAssemblies.Application)
            .ShouldNot()
            .HaveDependencyOnAny(ForbiddenInApplication)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailingTypes(result));
    }

    [Fact]
    public void Infrastructure_ReferencesOnlyApplicationAndDomainAmongTheLedgerAssemblies()
    {
        LayerAssemblies.LedgerReferencesOf(LayerAssemblies.Infrastructure)
            .ShouldBeSubsetOf(["Ledger.Application", "Ledger.Domain"]);
    }

    [Fact]
    public void Infrastructure_DoesNotDependOnTheExecutables()
    {
        var result = Types.InAssembly(LayerAssemblies.Infrastructure)
            .ShouldNot()
            .HaveDependencyOnAny(LayerAssemblies.ApiNamespace, LayerAssemblies.WorkerNamespace)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailingTypes(result));
    }

    [Fact]
    public void Api_DoesNotReferenceTheWorker()
    {
        LayerAssemblies.LedgerReferencesOf(LayerAssemblies.Api).ShouldNotContain("Ledger.Worker");
    }

    [Fact]
    public void Worker_DoesNotReferenceTheApi()
    {
        LayerAssemblies.LedgerReferencesOf(LayerAssemblies.Worker).ShouldNotContain("Ledger.Api");
    }

    [Fact]
    public void Executables_ReferenceOnlyApplicationInfrastructureAndDomainAmongTheLedgerAssemblies()
    {
        var allowed = new[] { "Ledger.Application", "Ledger.Domain", "Ledger.Infrastructure" };

        LayerAssemblies.LedgerReferencesOf(LayerAssemblies.Api).ShouldBeSubsetOf(allowed);
        LayerAssemblies.LedgerReferencesOf(LayerAssemblies.Worker).ShouldBeSubsetOf(allowed);
    }

    [Fact]
    public void Endpoints_DoNotDependOnDataAccessOrInfrastructure()
    {
        var result = Types.InAssembly(LayerAssemblies.Api)
            .That()
            .ResideInNamespaceStartingWith(LayerAssemblies.EndpointsNamespace)
            .ShouldNot()
            .HaveDependencyOnAny("Npgsql", "Dapper", LayerAssemblies.InfrastructureNamespace)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailingTypes(result));
    }

    [Fact]
    public void OnlyPersistenceTypesUseNpgsqlOrDapper()
    {
        var result = Types.InAssembly(LayerAssemblies.Infrastructure)
            .That()
            .DoNotResideInNamespaceStartingWith(LayerAssemblies.PersistenceNamespace)
            .ShouldNot()
            .HaveDependencyOnAny("Npgsql", "Dapper")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailingTypes(result));
    }

    [Fact]
    public void ExecutablesOutsidePersistenceTypesDoNotUseNpgsqlOrDapper()
    {
        var api = Types.InAssembly(LayerAssemblies.Api)
            .ShouldNot()
            .HaveDependencyOnAny("Npgsql", "Dapper")
            .GetResult();

        var worker = Types.InAssembly(LayerAssemblies.Worker)
            .ShouldNot()
            .HaveDependencyOnAny("Npgsql", "Dapper")
            .GetResult();

        api.IsSuccessful.ShouldBeTrue(FailingTypes(api));
        worker.IsSuccessful.ShouldBeTrue(FailingTypes(worker));
    }

    [Fact]
    public void TypesNamedHandler_AreSealed()
    {
        var handlers = Types.InAssembly(LayerAssemblies.Application)
            .That()
            .HaveNameEndingWith("Handler", StringComparison.Ordinal);

        handlers.GetTypes().ShouldNotBeEmpty();

        var notSealed = handlers
            .Should()
            .BeSealed()
            .GetResult();

        notSealed.IsSuccessful.ShouldBeTrue(FailingTypes(notSealed));
    }

    [Fact]
    public void TypesNamedHandler_ExposeASinglePublicMethodNamedHandleAsync()
    {
        var handlers = Types.InAssembly(LayerAssemblies.Application)
            .That()
            .HaveNameEndingWith("Handler", StringComparison.Ordinal)
            .GetTypes()
            .ToList();

        handlers.ShouldNotBeEmpty();

        var offenders = handlers
            .Where(handler => PublicDeclaredMethods(handler).Select(method => method.Name).ToList() is not ["HandleAsync"])
            .Select(handler => handler.FullName)
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void Handlers_TakeTheCancellationTokenLastInHandleAsync()
    {
        var offenders = Types.InAssembly(LayerAssemblies.Application)
            .That()
            .HaveNameEndingWith("Handler", StringComparison.Ordinal)
            .GetTypes()
            .Select(handler => PublicDeclaredMethods(handler).Single())
            .Where(method => method.GetParameters().LastOrDefault()?.ParameterType != typeof(CancellationToken))
            .Select(method => $"{method.DeclaringType?.FullName}.{method.Name}")
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void ClassesInTheUseCaseNamespaces_AreSealedHandlers()
    {
        var offenders = LayerAssemblies.Application
            .GetTypes()
            .Where(IsUseCaseClass)
            .Where(type => !type.IsSealed || !type.Name.EndsWith("Handler", StringComparison.Ordinal))
            .Select(type => type.FullName)
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void PortInterfaces_AreImplementedOnlyInInfrastructure()
    {
        var portNamespace = "Ledger.Application.Abstractions";

        var ports = Types.InAssembly(LayerAssemblies.Application)
            .That()
            .AreInterfaces()
            .GetTypes()
            .ToList();

        ports.ShouldNotBeEmpty();
        ports.Where(port => port.Namespace == portNamespace).ShouldNotBeEmpty();

        var implementedOutsideInfrastructure = new[]
            {
                LayerAssemblies.Domain, LayerAssemblies.Application, LayerAssemblies.Api, LayerAssemblies.Worker
            }
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type =>
                type is { IsClass: true, IsAbstract: false } && ports.Any(port => port.IsAssignableFrom(type)))
            .Select(type => type.FullName)
            .ToList();

        implementedOutsideInfrastructure.ShouldBeEmpty();
    }

    private static IEnumerable<MethodInfo> PublicDeclaredMethods(Type type)
    {
        return type
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName);
    }

    private static bool IsUseCaseClass(Type type)
    {
        return type is { IsClass: true, IsNested: false }
               && !(type.IsAbstract && type.IsSealed)
               && type.Namespace is { } typeNamespace
               && UseCaseNamespaces.Any(useCase => IsInNamespace(typeNamespace, useCase))
               && !IsRecord(type)
               && type.GetCustomAttribute<CompilerGeneratedAttribute>() is null;
    }

    private static bool IsInNamespace(string typeNamespace, string useCaseNamespace)
    {
        return typeNamespace == useCaseNamespace ||
               typeNamespace.StartsWith(useCaseNamespace + ".", StringComparison.Ordinal);
    }

    private static bool IsRecord(Type type)
    {
        return type.GetMethod("<Clone>$", AnyInstanceMember) is not null;
    }

    private static string FailingTypes(TestResult result)
    {
        var failing = result.FailingTypeNames ?? [];

        return $"Violating types: {string.Join(", ", failing)}";
    }
}
