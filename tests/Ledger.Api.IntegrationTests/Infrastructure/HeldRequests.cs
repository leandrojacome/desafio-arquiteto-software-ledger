using System.Net.Http.Headers;
using Ledger.Api.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ledger.Api.IntegrationTests.Infrastructure;

internal static class HeldRequests
{
    public const string Route = "/__test/hold";

    public const string BalanceRoute = "/__test/hold-balance";

    public const string StatementRoute = "/__test/hold-statement";

    public static void Map(
        IEndpointRouteBuilder routes,
        SemaphoreSlim entered,
        SemaphoreSlim gate,
        bool anonymous = false,
        RequestClass requestClass = RequestClass.Write,
        string route = Route)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(entered);
        ArgumentNullException.ThrowIfNull(gate);

        var held = routes.MapPost(route, async () =>
            {
                entered.Release();
                await gate.WaitAsync(TimeSpan.FromSeconds(30));

                return Results.Ok();
            })
            .WithRequestClass(requestClass);

        if (anonymous)
        {
            held.AllowAnonymous();
        }
        else
        {
            held.RequireAuthorization(requestClass == RequestClass.Write ? "ledger.write" : "ledger.read");
        }
    }

    public static async Task WhileHeldAsync(
        HttpClient client,
        SemaphoreSlim entered,
        SemaphoreSlim gate,
        Func<Task> whileHeld,
        string route = Route)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(entered);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(whileHeld);

        var holding = PostAsync(client, route);

        try
        {
            await entered.WaitAsync(TimeSpan.FromSeconds(30));
            await whileHeld();
        }
        finally
        {
            gate.Release();
            using var released = await holding;
        }
    }

    public static async Task<HttpResponseMessage> PostAsync(HttpClient client, string route = Route)
    {
        ArgumentNullException.ThrowIfNull(client);

        using var body = new StringContent("{}");
        body.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        return await client.PostAsync(route, body, CancellationToken.None);
    }
}
