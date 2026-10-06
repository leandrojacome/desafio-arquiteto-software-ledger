namespace Ledger.Api.IntegrationTests.Infrastructure;

internal static class MessagingSettings
{
    public static Dictionary<string, string?> For(
        PostgresFixture postgres,
        EmptyDatabase database,
        RabbitMqFixture? broker,
        IReadOnlyDictionary<string, string?>? overrides = null)
    {
        ArgumentNullException.ThrowIfNull(postgres);
        ArgumentNullException.ThrowIfNull(database);

        var values = new Dictionary<string, string?>(
            postgres.ConfigurationWith(new Dictionary<string, string?> { ["Postgres:Database"] = database.Name }))
        {
            ["RabbitMq:Host"] = broker?.Host ?? "127.0.0.1",
            ["RabbitMq:Port"] = (broker?.Port ?? 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RabbitMq:Username"] = RabbitMqFixture.Username,
            ["RabbitMq:Password"] = RabbitMqFixture.Password,
            ["RabbitMq:ConnectTimeoutSeconds"] = "2",
            ["RabbitMq:ReconnectMinSeconds"] = "1",
            ["RabbitMq:ReconnectMaxSeconds"] = "2",
            ["RabbitMq:Retention:Queue"] = $"test.retention.{Guid.NewGuid():N}",
            ["Outbox:IdlePollMs"] = "20",
            ["Outbox:LeaseSeconds"] = "5",
            ["Outbox:ConfirmTimeoutSeconds"] = "2",
            ["Outbox:MeasureIntervalSeconds"] = "1",
            ["Worker:FailureBackoff:MinSeconds"] = "1",
            ["Worker:FailureBackoff:MaxSeconds"] = "2",
            ["Serilog:MinimumLevel:Default"] = "Warning"
        };

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                values[key] = value;
            }
        }

        return values;
    }
}
