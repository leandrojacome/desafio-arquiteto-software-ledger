using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Ledger.Api.IntegrationTests.Security;

internal sealed class FakeIssuer : IAsyncDisposable
{
    private readonly RSA _key = RSA.Create(2048);
    private WebApplication? _app;
    private int _discoveryRequests;
    private int _keyRequests;

    private FakeIssuer(string keyId)
    {
        KeyId = keyId;
    }

    public string Address { get; private set; } = string.Empty;

    public string KeyId { get; }

    public int DiscoveryRequests => Volatile.Read(ref _discoveryRequests);

    public int KeyRequests => Volatile.Read(ref _keyRequests);

    public static async Task<FakeIssuer> StartAsync(string keyId = "issuer-key-1")
    {
        var issuer = new FakeIssuer(keyId);

        await issuer.RunAsync();

        return issuer;
    }

    public SigningCredentials Credentials() => TokenForge.Rsa(_key, KeyId);

    public async Task StopAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        _key.Dispose();
    }

    private async Task RunAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();

        var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");

        app.MapGet("/.well-known/openid-configuration", () =>
        {
            Interlocked.Increment(ref _discoveryRequests);

            return Results.Json(new Dictionary<string, string>
            {
                ["issuer"] = Address,
                ["jwks_uri"] = $"{Address}/jwks"
            });
        });

        app.MapGet("/jwks", () =>
        {
            Interlocked.Increment(ref _keyRequests);
            var parameters = _key.ExportParameters(false);

            return Results.Json(new
            {
                keys = new[]
                {
                    new
                    {
                        kty = "RSA",
                        use = "sig",
                        alg = "RS256",
                        kid = KeyId,
                        n = Base64UrlEncoder.Encode(parameters.Modulus),
                        e = Base64UrlEncoder.Encode(parameters.Exponent)
                    }
                }
            });
        });

        await app.StartAsync();

        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();

        Address = addresses?.Addresses.First().TrimEnd('/')
                  ?? throw new InvalidOperationException("The fake issuer did not report its address.");
        _app = app;
    }
}
