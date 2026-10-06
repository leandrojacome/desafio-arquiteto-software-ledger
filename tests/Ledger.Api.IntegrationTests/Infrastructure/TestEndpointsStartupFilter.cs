using Ledger.Api.RateLimiting;
using Ledger.Api.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Infrastructure;

internal sealed class TestEndpointsStartupFilter(Action<IEndpointRouteBuilder>? additional = null) : IStartupFilter
{
    public const string Open = "/__test/open";
    public const string Closed = "/__test/closed";
    public const string Read = "/__test/read";
    public const string Write = "/__test/write";
    public const string Provisioning = "/__test/provisioning";
    public const string Boom = "/__test/boom";
    public const string Transient = "/__test/transient";
    public const string Timeout = "/__test/timeout";
    public const string BadBody = "/__test/bad-body";
    public const string Echo = "/__test/echo";

    private const string EndpointRouteBuilderKey = "__EndpointRouteBuilder";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return app =>
        {
            next(app);

            if (app.Properties.TryGetValue(EndpointRouteBuilderKey, out var builder)
                && builder is IEndpointRouteBuilder routes)
            {
                Map(routes);
                additional?.Invoke(routes);
            }
            else
            {
                throw new InvalidOperationException("The route builder of the application was not found.");
            }
        };
    }

    private static void Map(IEndpointRouteBuilder routes)
    {
        routes.MapGet(Open, () => Results.Ok(new { ok = true })).AllowAnonymous();
        routes.MapGet(Closed, () => Results.Ok(new { ok = true }));
        routes.MapGet(Read, () => Results.Ok(new { ok = true }))
            .RequireAuthorization(AuthorizationPolicies.LedgerRead)
            .WithRequestClass(RequestClass.Balance);
        routes.MapPost(Write, () => Results.Ok(new { ok = true }))
            .RequireAuthorization(AuthorizationPolicies.LedgerWrite)
            .WithRequestClass(RequestClass.Write);
        routes.MapPost(Provisioning, () => Results.Ok(new { ok = true }))
            .RequireAuthorization(AuthorizationPolicies.AccountProvisioning)
            .WithRequestClass(RequestClass.Write);
        routes.MapGet(Boom, () => Task.FromException<IResult>(new InvalidOperationException("secret detail")));
        routes.MapGet(Transient, () => Task.FromException<IResult>(new TimeoutException("db.internal")));
        routes.MapGet(Timeout, HangThenFailAsync);
        routes.MapPost(BadBody, (BadBodyRequest request) => Results.Ok(request)).AllowAnonymous();
        routes.MapPost(Echo, async (HttpRequest request) =>
            {
                using var reader = new StreamReader(request.Body);

                return Results.Text(await reader.ReadToEndAsync(request.HttpContext.RequestAborted));
            })
            .AllowAnonymous();
    }

    private static async Task HangThenFailAsync(HttpContext context)
    {
        try
        {
            await Task.Delay(System.Threading.Timeout.Infinite, context.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            throw new NpgsqlException("connection reset while cancelling the command");
        }
    }

    internal sealed record BadBodyRequest(int Amount);
}
