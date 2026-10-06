using Ledger.Api.ErrorHandling;
using Ledger.Api.Security;
using Ledger.Api.Validation;
using Ledger.Domain.Shared;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Ledger.Api.Endpoints;

internal static class ReadResults
{
    private const string NoStore = "no-store";

    public static string ClientIdOf(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.User.FindFirst(ClaimNames.ClientId)?.Value
               ?? throw new InvalidOperationException("The read policy admits only callers that carry a client id.");
    }

    public static IResult Success<TBody>(HttpContext context, TBody body)
        where TBody : notnull
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.Headers.CacheControl = NoStore;

        return TypedResults.Ok(body);
    }

    public static IResult Failure(HttpContext context, Error error) =>
        Problem(context, ProblemFactory.FromError(context, error));

    public static IResult Invalid(HttpContext context, IReadOnlyList<ValidationIssue> issues) =>
        Problem(context, ProblemFactory.FromValidation(context, issues));

    private static ProblemHttpResult Problem(HttpContext context, ProblemDetails problem)
    {
        context.Response.Headers.CacheControl = NoStore;

        return TypedResults.Problem(problem);
    }
}
