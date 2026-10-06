using System.Diagnostics.CodeAnalysis;

namespace Ledger.Api.RateLimiting;

[SuppressMessage("Design", "CA1008",
    Justification = "A zero member would let a route without a declared class pass for a valid one; the default must stay outside the defined values.")]
internal enum RequestClass
{
    Write = 1,
    Balance = 2,
    Statement = 3
}

internal static class RequestClassExtensions
{
    public static TBuilder WithRequestClass<TBuilder>(this TBuilder builder, RequestClass requestClass)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithMetadata(new RequestClassMetadata(requestClass));
    }
}

internal sealed record RequestClassMetadata(RequestClass Class);
