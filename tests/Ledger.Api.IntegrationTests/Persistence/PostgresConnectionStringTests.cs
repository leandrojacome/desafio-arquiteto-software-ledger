using Ledger.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Persistence;

[Trait("Category", "Unit")]
public sealed class PostgresConnectionStringTests
{
    private const int InstancesOfTheApi = 6;
    private const int InstancesOfTheWorker = 2;
    private const int ReservedConnections = 20;
    private const int ServerLimit = 200;

    public static TheoryData<PostgresSource, string, string> ExpectedPerSource => new()
    {
        {
            PostgresSource.Write, "ledger-api-write",
            "-c timezone=UTC -c lock_timeout=1000 -c statement_timeout=2500 -c idle_in_transaction_session_timeout=5000"
        },
        {
            PostgresSource.Balance, "ledger-api-balance",
            "-c timezone=UTC -c statement_timeout=1500 -c idle_in_transaction_session_timeout=5000 -c default_transaction_read_only=on"
        },
        {
            PostgresSource.Statement, "ledger-api-statement",
            "-c timezone=UTC -c statement_timeout=1500 -c idle_in_transaction_session_timeout=5000 -c default_transaction_read_only=on"
        },
        {
            PostgresSource.Worker, "ledger-worker",
            "-c timezone=UTC -c lock_timeout=1000 -c statement_timeout=10000 -c idle_in_transaction_session_timeout=15000"
        },
        {
            PostgresSource.Migrator, "ledger-migrator",
            "-c timezone=UTC -c statement_timeout=300000 -c idle_in_transaction_session_timeout=300000"
        }
    };

    [Fact]
    public void AddLedgerPostgres_TurnsOffTheInfinityConversionsOfNpgsql()
    {
        AppContext.SetSwitch(NpgsqlRuntimeSwitches.DisableDateTimeInfinityConversions, false);

        try
        {
            new ServiceCollection().AddLedgerPostgres(new ConfigurationBuilder().Build(), [PostgresSource.Write]);

            AppContext.TryGetSwitch(NpgsqlRuntimeSwitches.DisableDateTimeInfinityConversions, out var disabled).ShouldBeTrue();
            disabled.ShouldBeTrue();
        }
        finally
        {
            NpgsqlRuntimeSwitches.Apply();
        }
    }

    [Theory]
    [MemberData(nameof(ExpectedPerSource))]
    public void Build_PutsTheApplicationNameAndTheServerSettingsOfTheSource(
        PostgresSource source,
        string applicationName,
        string serverOptions)
    {
        var builder = BuilderFor(source);

        builder.ApplicationName.ShouldBe(applicationName);
        builder.Options.ShouldBe(serverOptions);
    }

    [Theory]
    [MemberData(nameof(ExpectedPerSource))]
    public void Build_AppliesTheSettingsEverySourceShares(PostgresSource source, string applicationName, string serverOptions)
    {
        var builder = BuilderFor(source);

        applicationName.ShouldNotBeEmpty();
        serverOptions.ShouldNotBeEmpty();
        builder.Multiplexing.ShouldBeFalse();
        builder.TcpKeepAlive.ShouldBeTrue();
        builder.ConnectionIdleLifetime.ShouldBe(300);
        builder.ConnectionPruningInterval.ShouldBe(10);
        builder.MaxAutoPrepare.ShouldBe(20);
        builder.AutoPrepareMinUsages.ShouldBe(2);
        builder.IncludeErrorDetail.ShouldBeFalse();
        builder.GssEncryptionMode.ShouldBe(GssEncryptionMode.Disable);
    }

    [Fact]
    public void Build_GivesEachSourceItsOwnPoolAndTimeouts()
    {
        var write = BuilderFor(PostgresSource.Write);
        var balance = BuilderFor(PostgresSource.Balance);
        var statement = BuilderFor(PostgresSource.Statement);

        (write.MaxPoolSize, write.MinPoolSize, write.Timeout, write.CommandTimeout).ShouldBe((7, 2, 1, 2));
        (balance.MaxPoolSize, balance.MinPoolSize, balance.Timeout, balance.CommandTimeout).ShouldBe((8, 2, 1, 1));
        (statement.MaxPoolSize, statement.MinPoolSize, statement.Timeout, statement.CommandTimeout).ShouldBe((4, 1, 1, 1));
    }

    [Fact]
    public void ConnectionBudget_OfTheDefaultPools_FitsTheServerLimitOfTheCompose()
    {
        var apiPools = Options().Sources.Write.MaxPoolSize + Options().Sources.Balance.MaxPoolSize +
                       Options().Sources.Statement.MaxPoolSize;
        var budget = (InstancesOfTheApi * apiPools) +
                     (InstancesOfTheWorker * Options().Sources.Worker.MaxPoolSize) + ReservedConnections;

        budget.ShouldBe(144);
        budget.ShouldBeLessThanOrEqualTo(ServerLimit);
        ComposeServerLimit().ShouldBe(ServerLimit);
    }

    private static NpgsqlConnectionStringBuilder BuilderFor(PostgresSource source) =>
        new(PostgresConnectionString.Build(Options(), source));

    private static int ComposeServerLimit()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "docker-compose.yml")))
        {
            directory = directory.Parent;
        }

        var compose = File.ReadAllText(Path.Combine(directory!.FullName, "docker-compose.yml"));
        var marker = "max_connections=";
        var start = compose.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var length = compose[start..].TakeWhile(char.IsAsciiDigit).Count();

        return int.Parse(compose.AsSpan(start, length), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static PostgresOptions Options() =>
        new()
        {
            Host = "localhost",
            Database = "ledger",
            Sources = new PostgresSourcesOptions
            {
                Write = Source("ledger_api", 7, 2, 1, 2, 1000, 2500, 5000, false),
                Balance = Source("ledger_api", 8, 2, 1, 1, null, 1500, 5000, true),
                Statement = Source("ledger_api", 4, 1, 1, 1, null, 1500, 5000, true),
                Worker = Source("ledger_worker", 5, 1, 5, 10, 1000, 10_000, 15_000, false),
                Migrator = Source("ledger_migrator", 2, 0, 5, 300, null, 300_000, 300_000, false)
            }
        };

    private static PostgresSourceOptions Source(
        string username,
        int maxPool,
        int minPool,
        int connectionTimeout,
        int commandTimeout,
        int? lockTimeout,
        int statementTimeout,
        int idleInTransaction,
        bool readOnly) =>
        new()
        {
            Username = username,
            Password = "password",
            MaxPoolSize = maxPool,
            MinPoolSize = minPool,
            ConnectionTimeoutSeconds = connectionTimeout,
            CommandTimeoutSeconds = commandTimeout,
            LockTimeoutMs = lockTimeout,
            StatementTimeoutMs = statementTimeout,
            IdleInTransactionTimeoutMs = idleInTransaction,
            ReadOnly = readOnly
        };
}
