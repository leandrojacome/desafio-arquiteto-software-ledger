using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Ledger.Api.Security;

internal static class SecurityServiceCollectionExtensions
{
    public static IServiceCollection AddLedgerAuthentication(this IServiceCollection services)
    {
        services.AddOptions<JwtAuthenticationOptions>()
            .BindConfiguration(JwtAuthenticationOptions.SectionName)
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<JwtAuthenticationOptions>, JwtAuthenticationOptionsValidator>();
        services.ConfigureOptions<ConfigureJwtBearerOptions>();
        services.AddScoped<AuthenticationEvents>();

        services.AddOptions<ProvisioningOptions>()
            .BindConfiguration(ProvisioningOptions.SectionName)
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<ProvisioningOptions>, ProvisioningOptionsValidator>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.AddSingleton<IValidateOptions<JwtBearerOptions>, JwtBearerOptionsValidator>();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).ValidateOnStart();

        services.AddSingleton<IAuthorizationHandler, ScopeAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, ProvisioningClientHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, LedgerAuthorizationResultHandler>();

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.LedgerRead, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new ScopeRequirement(AuthorizationPolicies.LedgerRead)))
            .AddPolicy(AuthorizationPolicies.LedgerWrite, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new ScopeRequirement(AuthorizationPolicies.LedgerWrite)))
            .AddPolicy(AuthorizationPolicies.AccountProvisioning, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(
                    new ScopeRequirement(AuthorizationPolicies.LedgerWrite),
                    new ProvisioningClientRequirement()))
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireClaim(ClaimNames.ClientId)
                .Build());

        return services;
    }
}
