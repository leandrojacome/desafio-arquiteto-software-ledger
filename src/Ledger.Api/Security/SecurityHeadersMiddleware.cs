namespace Ledger.Api.Security;

internal sealed class SecurityHeadersMiddleware
{
    private const string NoSniff = "nosniff";
    private const string ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
    private const string ReferrerPolicyHeader = "Referrer-Policy";
    private const string ReferrerPolicy = "no-referrer";
    private const string StrictTransportSecurity = "max-age=31536000; includeSubDomains";
    private const string NoStore = "no-store";

    private readonly RequestDelegate _next;
    private readonly Func<object, Task> _writeHeaders;

    public SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        _next = next;

        var sendStrictTransportSecurity = !environment.IsDevelopment();

        _writeHeaders = state =>
        {
            var headers = ((HttpContext)state).Response.Headers;

            headers.XContentTypeOptions = NoSniff;
            headers.ContentSecurityPolicy = ContentSecurityPolicy;
            headers[ReferrerPolicyHeader] = ReferrerPolicy;

            if (sendStrictTransportSecurity)
            {
                headers.StrictTransportSecurity = StrictTransportSecurity;
            }

            headers.CacheControl = NoStore;

            return Task.CompletedTask;
        };
    }

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.OnStarting(_writeHeaders, context);

        return _next(context);
    }
}
