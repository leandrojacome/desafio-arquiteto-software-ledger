using System.Globalization;
using System.Text;

namespace Ledger.Infrastructure.Tests.Observability.Support;

internal static class LeakScanner
{
    public static IReadOnlyList<string> Find(string haystack, IEnumerable<string> canaries)
    {
        var found = new List<string>();

        foreach (var canary in canaries)
        {
            if (Forms(canary).Any(form => haystack.Contains(form, StringComparison.OrdinalIgnoreCase)))
            {
                found.Add(canary);
            }
        }

        return found;
    }

    public static string TextOf(TelemetryCapture capture)
    {
        var builder = new StringBuilder();

        foreach (var measurement in capture.All)
        {
            builder.Append(measurement.Instrument).Append(' ').Append(measurement.Unit).Append(' ');

            foreach (var tag in measurement.Tags)
            {
                builder.Append(tag.Key).Append('=').Append(Convert.ToString(tag.Value, CultureInfo.InvariantCulture)).Append(' ');
            }

            builder.Append('\n');
        }

        foreach (var activity in capture.Activities)
        {
            builder.Append(activity.OperationName).Append(' ').Append(activity.DisplayName).Append(' ');

            foreach (var tag in activity.TagObjects)
            {
                builder.Append(tag.Key).Append('=').Append(Convert.ToString(tag.Value, CultureInfo.InvariantCulture)).Append(' ');
            }

            foreach (var baggage in activity.Baggage)
            {
                builder.Append(baggage.Key).Append('=').Append(baggage.Value).Append(' ');
            }

            foreach (var link in activity.Links)
            {
                builder.Append(link.Context.TraceId).Append(' ');
            }

            builder.Append(activity.StatusDescription).Append('\n');
        }

        return builder.ToString();
    }

    private static IEnumerable<string> Forms(string canary)
    {
        var bytes = Encoding.UTF8.GetBytes(canary);

        yield return canary;
        yield return Convert.ToHexString(bytes);
        yield return Convert.ToBase64String(bytes);
    }
}
