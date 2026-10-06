using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing.Patterns;

namespace Ledger.Api.Security;

internal static class RouteTemplates
{
    public const string Unmatched = "(unmatched)";

    public static string Of(HttpContext context)
    {
        return context.GetEndpoint() is RouteEndpoint endpoint ? Render(endpoint.RoutePattern) : Unmatched;
    }

    public static bool AllowsAnonymous(HttpContext context)
    {
        return context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null;
    }

    public static string Render(RoutePattern pattern)
    {
        var builder = new StringBuilder();

        foreach (var segment in pattern.PathSegments)
        {
            builder.Append('/');

            foreach (var part in segment.Parts)
            {
                switch (part)
                {
                    case RoutePatternLiteralPart literal:
                        builder.Append(literal.Content);
                        break;
                    case RoutePatternParameterPart parameter:
                        builder.Append('{').Append(parameter.Name).Append('}');
                        break;
                    case RoutePatternSeparatorPart separator:
                        builder.Append(separator.Content);
                        break;
                }
            }
        }

        return builder.Length == 0 ? "/" : builder.ToString();
    }
}
