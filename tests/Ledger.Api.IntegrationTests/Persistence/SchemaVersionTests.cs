using Ledger.Infrastructure.Persistence;

namespace Ledger.Api.IntegrationTests.Persistence;

[Trait("Category", "Unit")]
public sealed class SchemaVersionTests
{
    [Fact]
    public void Expected_IsTheHighestPrefixAmongTheEmbeddedMigrations()
    {
        var resources = typeof(SchemaVersion).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith("Ledger.Infrastructure.Persistence.Migrations.", StringComparison.Ordinal))
            .ToList();

        var highest = resources
            .Select(name => name["Ledger.Infrastructure.Persistence.Migrations.".Length..][..4])
            .Select(prefix => int.Parse(prefix, System.Globalization.CultureInfo.InvariantCulture))
            .Max();

        SchemaVersion.Expected.ShouldBe(highest);
        SchemaVersion.Expected.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void Highest_ReadsTheNumericPrefixOfTheJournalNames()
    {
        var applied = new[]
        {
            "Ledger.Infrastructure.Persistence.Migrations.0001_initial_schema.sql",
            "Ledger.Infrastructure.Persistence.Migrations.0003_grant_schema_version_and_audit_read.sql",
            "Ledger.Infrastructure.Persistence.Migrations.0002_grant_privileges.sql"
        };

        SchemaVersion.Highest(applied).ShouldBe(3);
    }

    [Fact]
    public void Highest_IgnoresNamesWithoutAVersionPrefix()
    {
        SchemaVersion.Highest(["readme.txt", "Migrations.abcd_nothing.sql"]).ShouldBe(0);
    }

    [Fact]
    public void Highest_OfAnEmptyJournal_IsZero()
    {
        SchemaVersion.Highest([]).ShouldBe(0);
    }

    [Theory]
    [InlineData(3, 3, true)]
    [InlineData(3, 4, true)]
    [InlineData(3, 2, false)]
    [InlineData(3, null, false)]
    public void IsSatisfiedBy_RequiresTheCurrentVersionToReachTheExpectedOne(int expected, int? current, bool satisfied)
    {
        SchemaVersion.IsSatisfiedBy(expected, current).ShouldBe(satisfied);
    }
}
