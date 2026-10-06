using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Ledger.Infrastructure.Tests.Observability.Support;

internal static class TestActivities
{
    [SuppressMessage("Reliability", "CA2000", Justification = "The caller owns the activity and disposes it.")]
    public static Activity Start(string name, params (string Key, string Value)[] tags)
    {
        var activity = new Activity(name);

        foreach (var (key, value) in tags)
        {
            activity.SetTag(key, value);
        }

        return activity.Start();
    }
}
