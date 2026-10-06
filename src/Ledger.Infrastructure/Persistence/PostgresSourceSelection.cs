namespace Ledger.Infrastructure.Persistence;

internal sealed class PostgresSourceSelection
{
    public PostgresSourceSelection(IEnumerable<PostgresSource> sources)
    {
        Sources = sources.Distinct().ToArray();

        if (Sources.Count == 0)
        {
            throw new ArgumentException("At least one PostgreSQL source must be informed.", nameof(sources));
        }
    }

    public IReadOnlyList<PostgresSource> Sources { get; }

    public PostgresSource WriteSource
    {
        get
        {
            if (Sources.Contains(PostgresSource.Write))
            {
                return PostgresSource.Write;
            }

            return Sources.Contains(PostgresSource.Worker) ? PostgresSource.Worker : PostgresSource.Write;
        }
    }

    public PostgresSource ReadinessSource
    {
        get
        {
            if (Sources.Contains(PostgresSource.Statement))
            {
                return PostgresSource.Statement;
            }

            return Sources.Contains(PostgresSource.Worker) ? PostgresSource.Worker : Sources[0];
        }
    }
}
