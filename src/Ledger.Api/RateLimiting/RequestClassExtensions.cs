namespace Ledger.Api.RateLimiting;

internal static class RequestClassExtensions
{
    public static TBuilder WithRequestClass<TBuilder>(this TBuilder builder, RequestClass requestClass)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithMetadata(new RequestClassMetadata(requestClass));
    }
}
