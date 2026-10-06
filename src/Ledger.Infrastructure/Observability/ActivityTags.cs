using System.Diagnostics;
using Ledger.Application;

namespace Ledger.Infrastructure.Observability;

internal static class ActivityTags
{
    public const string ClientId = ActivityTagNames.ClientId;
    public const string CorrelationId = ActivityTagNames.CorrelationId;

    public static string? Find(Activity? activity, string tagName)
    {
        for (var current = activity; current is not null; current = current.Parent)
        {
            if (current.GetTagItem(tagName) is string value && value.Length > 0)
            {
                return value;
            }
        }

        return null;
    }
}
