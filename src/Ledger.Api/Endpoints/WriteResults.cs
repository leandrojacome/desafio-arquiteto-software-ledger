using System.Diagnostics;
using Ledger.Api.Contracts;
using Ledger.Api.ErrorHandling;
using Ledger.Api.Middleware;
using Ledger.Api.Security;
using Ledger.Api.Validation;
using Ledger.Application.Accounts;
using Ledger.Application.Entries;
using Ledger.Domain.Shared;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Ledger.Api.Endpoints;

internal static class WriteResults
{
    private const string NoStore = "no-store";
    private const string IdempotentReplayed = ApiConstants.IdempotentReplayedHeader;
    private const int TraceParentLength = 55;
    private const string PayloadTooLargeCode = "PAYLOAD_TOO_LARGE";
    private const string UnsupportedMediaTypeCode = "UNSUPPORTED_MEDIA_TYPE";

    public static string ClientIdOf(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.User.FindFirst(ClaimNames.ClientId)?.Value
               ?? throw new InvalidOperationException("The write policy admits only callers that carry a client id.");
    }

    public static string CorrelationIdOf(HttpContext context) => CorrelationIdMiddleware.Resolve(context);

    public static string? TraceParentOf()
    {
        var activity = Activity.Current;

        if (activity is null || activity.IdFormat != ActivityIdFormat.W3C || activity.Id is not { } id)
        {
            return null;
        }

        return id.Length > TraceParentLength ? id[..TraceParentLength] : id;
    }

    public static IResult CreatedEntry(HttpContext context, EntryOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(outcome);

        context.Response.Headers.CacheControl = NoStore;

        if (outcome.IsReplay)
        {
            context.Response.Headers[IdempotentReplayed] = "true";
        }

        var location = $"{ApiConstants.V1RoutePrefix}/accounts/{outcome.Entry.AccountId}/entries";

        return TypedResults.Created(location, EntryResponse.From(outcome.Entry));
    }

    public static IResult CreatedAccount(HttpContext context, CreatedAccount account)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(account);

        context.Response.Headers.CacheControl = NoStore;

        if (account.IsReplay)
        {
            context.Response.Headers[IdempotentReplayed] = "true";
        }

        var location = $"{ApiConstants.V1RoutePrefix}/accounts/{account.AccountId}/balance";

        return TypedResults.Created(location, CreateAccountResponse.From(account));
    }

    public static IResult Failure(HttpContext context, Error error) =>
        Problem(context, ProblemFactory.FromError(context, error));

    public static IResult Invalid(HttpContext context, IReadOnlyList<ValidationIssue> issues) =>
        Problem(context, ProblemFactory.FromValidation(context, issues));

    public static IResult PayloadTooLarge(HttpContext context) =>
        Problem(context, ProblemFactory.Create(
            context,
            StatusCodes.Status413PayloadTooLarge,
            PayloadTooLargeCode,
            $"O corpo da requisição não pode exceder {ApiConstants.MaxRequestBodyBytes} bytes."));

    public static IResult UnsupportedMediaType(HttpContext context) =>
        Problem(context, ProblemFactory.Create(
            context,
            StatusCodes.Status415UnsupportedMediaType,
            UnsupportedMediaTypeCode,
            "O 'Content-Type' deve ser 'application/json'."));

    public static IReadOnlyList<ValidationIssue> Combine(
        IReadOnlyList<ValidationIssue> bodyIssues,
        ValidationIssue? headerIssue)
    {
        if (headerIssue is null)
        {
            return bodyIssues;
        }

        return [.. bodyIssues, headerIssue];
    }

    private static ProblemHttpResult Problem(HttpContext context, ProblemDetails problem)
    {
        context.Response.Headers.CacheControl = NoStore;

        return TypedResults.Problem(problem);
    }
}
