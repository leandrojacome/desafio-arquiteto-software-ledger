using System.Globalization;
using System.Security.Cryptography;
using Ledger.Application.Abstractions;
using Ledger.Application.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Security;

internal sealed class CursorOptions
{
    public const string SectionName = "Security:Cursor";
    public const string SigningKeyName = $"{SectionName}:SigningKey";
    public const int SigningKeySize = 32;

    [Sensitive] public string? SigningKey { get; init; }

    internal byte[]? DecodeSigningKey()
    {
        if (string.IsNullOrWhiteSpace(SigningKey))
        {
            return null;
        }

        var buffer = new byte[(SigningKey.Length * 3 / 4) + 3];

        if (!Convert.TryFromBase64String(SigningKey.Trim(), buffer, out var written) || written != SigningKeySize)
        {
            return null;
        }

        var key = buffer.AsSpan(0, written).ToArray();
        CryptographicOperations.ZeroMemory(buffer);

        return key;
    }
}

internal sealed class CursorOptionsValidator : IValidateOptions<CursorOptions>
{
    public ValidateOptionsResult Validate(string? name, CursorOptions options)
    {
        if (options.DecodeSigningKey() is not null)
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{CursorOptions.SigningKeyName} is required and must be the base64 of exactly {CursorOptions.SigningKeySize} bytes."));
    }
}

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
