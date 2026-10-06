using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
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
