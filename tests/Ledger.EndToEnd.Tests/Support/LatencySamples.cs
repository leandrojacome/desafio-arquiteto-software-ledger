using System.Globalization;

namespace Ledger.EndToEnd.Tests.Support;

internal sealed class LatencySamples(IEnumerable<TimeSpan> samples)
{
    private const string FactorVariable = "LEDGER_E2E_LATENCY_FACTOR";

    private readonly List<double> _milliseconds = [.. samples.Select(sample => sample.TotalMilliseconds).Order()];

    public static double Factor()
    {
        var text = Environment.GetEnvironmentVariable(FactorVariable);

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var factor) && factor > 0
            ? factor
            : 1d;
    }

    public double Percentile(double fraction)
    {
        if (_milliseconds.Count == 0)
        {
            throw new InvalidOperationException("There are no latency samples to summarize.");
        }

        var index = (int)Math.Ceiling(fraction * _milliseconds.Count) - 1;

        return _milliseconds[Math.Clamp(index, 0, _milliseconds.Count - 1)];
    }

    public string Describe(string name, double budgetMilliseconds)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{name}: p50 {Percentile(0.50):0.0} ms, p99 {Percentile(0.99):0.0} ms, budget {budgetMilliseconds:0} ms, factor {Factor():0.##}. These budgets are the proposed targets of docs/02-contexto-e-requisitos/requisitos-nao-funcionais.md, not measurements of production.");
    }
}
