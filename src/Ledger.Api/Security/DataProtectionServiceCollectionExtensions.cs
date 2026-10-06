using System.Collections.Concurrent;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;

namespace Ledger.Api.Security;

internal static class DataProtectionServiceCollectionExtensions
{
    public static IServiceCollection AddLedgerDataProtection(this IServiceCollection services)
    {
        services.AddDataProtection().SetApplicationName(ApiConstants.ServiceName);

        services.AddSingleton<InMemoryKeyRepository>();
        services.AddOptions<KeyManagementOptions>()
            .Configure<InMemoryKeyRepository>((options, repository) =>
            {
                options.XmlRepository = repository;
                options.XmlEncryptor = new NullXmlEncryptor();
            });

        return services;
    }
}

internal sealed class InMemoryKeyRepository : IXmlRepository
{
    private readonly ConcurrentQueue<XElement> _elements = new();

    public IReadOnlyCollection<XElement> GetAllElements() => [.. _elements];

    public void StoreElement(XElement element, string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(element);

        _elements.Enqueue(element);
    }
}
