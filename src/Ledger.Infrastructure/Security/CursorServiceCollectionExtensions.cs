using Ledger.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Security;

internal static class CursorServiceCollectionExtensions
{
    public static IServiceCollection AddLedgerCursor(
        this IServiceCollection services,
        IConfiguration configuration,
        bool validateOnStart = true)
    {
        var options = services.AddOptions<CursorOptions>()
            .Bind(configuration.GetSection(CursorOptions.SectionName));

        if (validateOnStart)
        {
            options.ValidateOnStart();
        }

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<CursorOptions>, CursorOptionsValidator>());
        services.TryAddSingleton<IStatementCursorProtector>(CreateCursorProtector);

        return services;
    }

    private static IStatementCursorProtector CreateCursorProtector(IServiceProvider provider)
    {
        var options = provider.GetRequiredService<IOptions<CursorOptions>>().Value;
        var signingKey = options.DecodeSigningKey()
                         ?? throw new InvalidOperationException(
                             $"{CursorOptions.SigningKeyName} is required and must be the base64 of exactly {CursorOptions.SigningKeySize} bytes.");

        return HmacStatementCursorProtector.Create(signingKey);
    }
}
