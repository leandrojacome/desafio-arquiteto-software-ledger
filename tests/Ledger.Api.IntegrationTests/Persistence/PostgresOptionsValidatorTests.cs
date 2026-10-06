using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Infrastructure.Persistence;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Persistence;

[Trait("Category", "Unit")]
public sealed class PostgresOptionsValidatorTests
{
    private const string Secret = "do-not-print-this-secret";

    private static readonly PostgresSource[] ApiSources =
        [PostgresSource.Write, PostgresSource.Balance, PostgresSource.Statement];

    [Fact]
    public void Validate_WithAValidConfiguration_Succeeds()
    {
        var result = Validate(ValidOptions(), Environments.Development, ApiSources);

        result.Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("", "ledger", 5432)]
    [InlineData("localhost", "", 5432)]
    [InlineData("localhost", "ledger", 0)]
    [InlineData("localhost", "ledger", 65_536)]
    public void Validate_WithAnInvalidServerAddress_NamesTheSection(string host, string database, int port)
    {
        var options = new PostgresOptions { Host = host, Database = database, Port = port, Sources = ValidSources() };

        var result = Validate(options, Environments.Development, ApiSources);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Postgres");
    }

    [Fact]
    public void Validate_WithoutAPasswordForARequiredSource_NamesTheSourcePath()
    {
        var options = ValidOptions(write: Source(password: string.Empty));

        var result = Validate(options, Environments.Development, ApiSources);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Postgres:Sources:Write");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public void Validate_WithAPoolOutsideTheRange_Fails(int maxPoolSize)
    {
        var options = ValidOptions(write: Source(maxPoolSize: maxPoolSize));

        var result = Validate(options, Environments.Development, ApiSources);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Postgres:Sources:Write");
    }

    [Fact]
    public void Validate_WhenTheMinimumPoolIsAboveTheMaximum_NamesBothSettings()
    {
        var options = ValidOptions(write: Source(maxPoolSize: 3, minPoolSize: 4));

        var result = Validate(options, Environments.Development, ApiSources);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Postgres:Sources:Write");
        result.FailureMessage.ShouldContain("MinPoolSize");
        result.FailureMessage.ShouldContain("MaxPoolSize");
    }

    [Fact]
    public void Validate_WhenTheMinimumPoolEqualsTheMaximum_Succeeds()
    {
        var options = ValidOptions(write: Source(maxPoolSize: 4, minPoolSize: 4));

        Validate(options, Environments.Development, ApiSources).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_OnlyChecksTheSourcesTheExecutableDeclares()
    {
        var options = ValidOptions(statement: new PostgresSourceOptions());

        Validate(options, Environments.Development, [PostgresSource.Write, PostgresSource.Balance])
            .Succeeded.ShouldBeTrue();
        Validate(options, Environments.Development, ApiSources).Failed.ShouldBeTrue();
    }

    [Fact]
    public void Validate_InProduction_RefusesAnySslModeButVerifyFull()
    {
        var options = ValidOptions(sslMode: SslMode.Require);

        var result = Validate(options, Environments.Production, ApiSources);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Postgres:SslMode");
        result.FailureMessage.ShouldContain("VerifyFull");
    }

    [Fact]
    public void Validate_InProduction_RefusesErrorDetail()
    {
        var options = ValidOptions(sslMode: SslMode.VerifyFull, includeErrorDetail: true);

        var result = Validate(options, Environments.Production, ApiSources);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Postgres:IncludeErrorDetail");
    }

    [Fact]
    public void Validate_InProduction_AcceptsVerifyFullWithoutErrorDetail()
    {
        var options = ValidOptions(sslMode: SslMode.VerifyFull);

        Validate(options, Environments.Production, ApiSources).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData("testing")]
    public void Validate_InADevelopmentOrTestEnvironment_AllowsUnencryptedConnectionsAndErrorDetail(string environment)
    {
        var options = ValidOptions(sslMode: SslMode.Disable, includeErrorDetail: true);

        Validate(options, environment, ApiSources).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Homolog")]
    [InlineData("")]
    public void Validate_InAnyOtherEnvironment_AppliesTheProductionRules(string environment)
    {
        var options = ValidOptions(sslMode: SslMode.Disable, includeErrorDetail: true);

        var result = Validate(options, environment, ApiSources);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Postgres:SslMode");
        result.FailureMessage.ShouldContain("Postgres:IncludeErrorDetail");
    }

    [Fact]
    public void Validate_NeverPrintsThePassword()
    {
        var options = ValidOptions(
            sslMode: SslMode.Disable,
            includeErrorDetail: true,
            write: Source(password: Secret, maxPoolSize: 2, minPoolSize: 9));

        var result = Validate(options, Environments.Production, ApiSources);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldNotContain(Secret);
    }

    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(
        PostgresOptions options,
        string environment,
        PostgresSource[] required)
    {
        var validator = new PostgresOptionsValidator(required, new TestHostEnvironment(environment));

        return validator.Validate(null, options);
    }

    private static PostgresOptions ValidOptions(
        SslMode sslMode = SslMode.Disable,
        bool includeErrorDetail = false,
        PostgresSourceOptions? write = null,
        PostgresSourceOptions? statement = null)
    {
        return new PostgresOptions
        {
            Host = "localhost",
            Database = "ledger",
            SslMode = sslMode,
            IncludeErrorDetail = includeErrorDetail,
            Sources = ValidSources(write, statement)
        };
    }

    private static PostgresSourcesOptions ValidSources(PostgresSourceOptions? write = null, PostgresSourceOptions? statement = null)
    {
        return new PostgresSourcesOptions
        {
            Write = write ?? Source(),
            Balance = Source(),
            Statement = statement ?? Source(),
            Worker = Source(),
            Migrator = Source()
        };
    }

    private static PostgresSourceOptions Source(
        string password = "password",
        int maxPoolSize = 7,
        int minPoolSize = 2) =>
        new() { Username = "ledger_api", Password = password, MaxPoolSize = maxPoolSize, MinPoolSize = minPoolSize };
}
