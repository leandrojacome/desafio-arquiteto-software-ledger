namespace Ledger.Architecture.Tests.Layers;

[Trait("Category", "Architecture")]
public sealed class InfrastructurePublicSurfaceTests
{
    private static readonly string[] AllowedPublicTypes =
    [
        "Ledger.Infrastructure.DependencyInjection",
        "Ledger.Infrastructure.Health.WorkerHealthServiceCollectionExtensions",
        "Ledger.Infrastructure.Health.WorkerHealthTags",
        "Ledger.Infrastructure.Hosting.HostEnvironmentExtensions",
        "Ledger.Infrastructure.Messaging.OutboxOptions",
        "Ledger.Infrastructure.Observability.LoggingServiceCollectionExtensions",
        "Ledger.Infrastructure.Observability.StartupLogger",
        "Ledger.Infrastructure.Observability.TelemetryServiceCollectionExtensions",
        "Ledger.Infrastructure.Persistence.PostgresSource",
        "Ledger.Infrastructure.Resilience.ResilienceHealthOptions",
        "Ledger.Infrastructure.Resilience.ResilienceOptions",
        "Ledger.Infrastructure.Resilience.ResilienceRetryOptions"
    ];

    [Fact]
    public void Infrastructure_ExposesExactlyTheTypesTheHostsNeed()
    {
        var exported = LayerAssemblies.Infrastructure
            .GetExportedTypes()
            .Select(type => type.FullName ?? type.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        exported.ShouldBe(AllowedPublicTypes.Order(StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void Infrastructure_KeepsTheClassesOfPersistenceMessagingAndSecurityInternal()
    {
        var leaks = LayerAssemblies.Infrastructure
            .GetExportedTypes()
            .Where(type => type.Namespace is { } ns
                           && (ns.EndsWith(".Security", StringComparison.Ordinal)
                               || ns.Contains(".Persistence", StringComparison.Ordinal)
                               || ns.EndsWith(".Messaging", StringComparison.Ordinal)
                               || ns.EndsWith(".Audit", StringComparison.Ordinal)))
            .Select(type => type.FullName)
            .Where(name => name is not ("Ledger.Infrastructure.Persistence.PostgresSource" or "Ledger.Infrastructure.Messaging.OutboxOptions"))
            .ToList();

        leaks.ShouldBeEmpty();
    }
}
