using Microsoft.Extensions.Options;

namespace Ledger.Api.Security;

internal static class ForwardedHeadersSetup
{
    public static IServiceCollection AddLedgerForwardedHeaders(this IServiceCollection services)
    {
        services.AddOptions<ForwardedHeadersSettings>()
            .BindConfiguration(ForwardedHeadersSettings.SectionName)
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<ForwardedHeadersSettings>, ForwardedHeadersSettingsValidator>();
        services.AddSingleton<IConfigureOptions<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>, ConfigureForwardedHeadersOptions>();

        return services;
    }

    public static bool IsEnabled(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.GetRequiredService<IOptions<ForwardedHeadersSettings>>().Value.KnownNetworks.Length > 0;
    }
}
