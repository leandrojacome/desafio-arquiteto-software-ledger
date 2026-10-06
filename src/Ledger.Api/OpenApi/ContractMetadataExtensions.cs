using Ledger.Api.Contracts;

namespace Ledger.Api.OpenApi;

internal static class ContractMetadataExtensions
{
    public static RouteHandlerBuilder ProducesLedgerProblem(
        this RouteHandlerBuilder builder,
        int status,
        params string[] codes)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .Produces<ProblemResponse>(status, OpenApiNames.ProblemMediaType)
            .WithMetadata(new ProblemCodesMetadata(status, codes));
    }

    public static RouteHandlerBuilder WithRequestBody<TRequest>(this RouteHandlerBuilder builder, bool isRequired)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithMetadata(new RequestBodyMetadata(typeof(TRequest), isRequired));
    }

    public static RouteHandlerBuilder WithIdempotencyKey(this RouteHandlerBuilder builder, bool isRequired)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithMetadata(new IdempotencyKeyMetadata(isRequired));
    }

    public static RouteHandlerBuilder WithQueryParameter(
        this RouteHandlerBuilder builder,
        QueryParameterMetadata parameter)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithMetadata(parameter);
    }
}
