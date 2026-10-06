using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Api.IntegrationTests.Infrastructure;

internal static class ServiceDecoration
{
    public static void Decorate<TService>(
        this IServiceCollection services,
        Func<TService, IServiceProvider, TService> decorator)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(decorator);

        var original = services.Last(descriptor => descriptor.ServiceType == typeof(TService));

        services.Remove(original);
        services.Add(ServiceDescriptor.Describe(
            typeof(TService),
            provider => decorator(Resolve<TService>(original, provider), provider),
            original.Lifetime));
    }

    private static TService Resolve<TService>(ServiceDescriptor descriptor, IServiceProvider provider)
        where TService : class
    {
        if (descriptor.ImplementationInstance is TService instance)
        {
            return instance;
        }

        if (descriptor.ImplementationFactory is { } factory)
        {
            return (TService)factory(provider);
        }

        var implementationType = descriptor.ImplementationType ??
                                 throw new InvalidOperationException(
                                     $"The registration of {typeof(TService).Name} cannot be decorated.");

        return (TService)ActivatorUtilities.CreateInstance(provider, implementationType);
    }
}
