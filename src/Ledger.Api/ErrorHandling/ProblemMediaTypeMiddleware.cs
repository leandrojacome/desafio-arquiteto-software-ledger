namespace Ledger.Api.ErrorHandling;

internal sealed class ProblemMediaTypeMiddleware(RequestDelegate next)
{
    private const string ProblemMediaType = "application/problem+json";
    private const string ProblemMediaTypeWithCharset = "application/problem+json; charset=utf-8";

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.OnStarting(DeclareCharset, context);

        return next(context);
    }

    private static Task DeclareCharset(object state)
    {
        var response = ((HttpContext)state).Response;

        if (string.Equals(response.ContentType, ProblemMediaType, StringComparison.OrdinalIgnoreCase))
        {
            response.ContentType = ProblemMediaTypeWithCharset;
        }

        return Task.CompletedTask;
    }
}
