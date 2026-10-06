using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Ledger.Api.Security;

internal sealed class ConfigureJwtBearerOptions(IOptions<JwtAuthenticationOptions> authentication)
    : IConfigureNamedOptions<JwtBearerOptions>
{
    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    private static readonly string[] AccessTokenTypes = ["at+jwt", "application/at+jwt"];

    public void Configure(JwtBearerOptions options) => Configure(Options.DefaultName, options);

    public void Configure(string? name, JwtBearerOptions options)
    {
        if (name != JwtBearerDefaults.AuthenticationScheme)
        {
            return;
        }

        var settings = authentication.Value;

        options.MapInboundClaims = false;
        options.IncludeErrorDetails = false;
        options.EventsType = typeof(AuthenticationEvents);
        options.RequireHttpsMetadata = settings.RequireHttpsMetadata;
        options.TokenValidationParameters = BuildValidationParameters(settings);

        if (settings.Mode == AuthenticationMode.Authority)
        {
            options.Authority = settings.Authority;
        }
    }

    private static TokenValidationParameters BuildValidationParameters(JwtAuthenticationOptions settings)
    {
        var parameters = new TokenValidationParameters
        {
            ValidIssuer = settings.Issuer,
            ValidAudience = settings.Audience,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ClockSkew = ClockSkew,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256, SecurityAlgorithms.EcdsaSha256]
        };

        if (settings.RequireAccessTokenType)
        {
            parameters.ValidTypes = AccessTokenTypes;
        }

        if (settings.Mode != AuthenticationMode.LocalKey)
        {
            return parameters;
        }

        if (!string.IsNullOrWhiteSpace(settings.LocalKey.PublicKeyPath))
        {
            parameters.IssuerSigningKey = LoadPublicKey(settings.LocalKey.PublicKeyPath);
        }
        else
        {
            parameters.ValidAlgorithms = [SecurityAlgorithms.HmacSha256];
            parameters.IssuerSigningKey =
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.LocalKey.SigningKey ?? string.Empty));
        }

        return parameters;
    }

    private static SecurityKey LoadPublicKey(string path)
    {
        var loaded = PublicKeyLoader.Load(path);

        if (!loaded.IsLoaded)
        {
            throw new OptionsValidationException(
                JwtBearerDefaults.AuthenticationScheme,
                typeof(JwtBearerOptions),
                [$"{JwtAuthenticationOptions.SectionName}:LocalKey:PublicKeyPath does not hold a usable public key ({loaded.Problem})."]);
        }

        return loaded.Key;
    }
}
