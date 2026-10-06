using System.Reflection;
using System.Text.RegularExpressions;
using Ledger.Architecture.Tests.Layers;

namespace Ledger.Architecture.Tests.Hygiene;

[Trait("Category", "Architecture")]
public sealed partial class LedgerEntriesAreNeverMutatedTests
{
    private const BindingFlags AnyStaticField = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
                                                | BindingFlags.DeclaredOnly;

    private static readonly string[] ImmutableTables = ["ledger_entries", "audit_log"];

    private static readonly string[] OutboxUpdates =
        ["ClaimOutboxBatchSql", "MarkOutboxPublishedSql", "ReleaseOutboxSql"];

    private static readonly string[] OutboxDeletes = ["PruneOutboxSql"];

    private static readonly string[] AccountUpdates = ["UpdateRewrappedSql"];

    public static TheoryData<string, string> ForbiddenStatements() => new()
    {
        { "UPDATE ledger_entries SET amount = 1", "ledger_entries" },
        { "update ledger_entries as e set amount = 1", "ledger_entries" },
        { "DELETE FROM ledger_entries WHERE true", "ledger_entries" },
        { "TRUNCATE ledger_entries", "ledger_entries" },
        { "TRUNCATE TABLE ledger_entries CASCADE", "ledger_entries" },
        { "UPDATE audit_log SET outcome = 'SUCCESS'", "audit_log" },
        { "DELETE FROM audit_log", "audit_log" },
        { "TRUNCATE audit_log", "audit_log" }
    };

    [Theory]
    [MemberData(nameof(ForbiddenStatements))]
    public void TheGuard_RecognizesEveryForbiddenStatementAgainstATable(string sql, string table)
    {
        Mutated(sql).ShouldContain(table);
    }

    [Theory]
    [InlineData("INSERT INTO ledger_entries (id) VALUES (@id)")]
    [InlineData("SELECT * FROM ledger_entries WHERE account_id = @account_id")]
    [InlineData("WITH applied AS (UPDATE account_balances SET balance = 1 RETURNING 1) INSERT INTO ledger_entries SELECT 1")]
    [InlineData("INSERT INTO audit_log (event_type) VALUES ('integrity.run_completed')")]
    public void TheGuard_AcceptsTheStatementsTheLedgerNeeds(string sql)
    {
        Mutated(sql).Intersect(ImmutableTables).ShouldBeEmpty();
    }

    [Fact]
    public void NoProductionSql_UpdatesDeletesOrTruncatesTheLedgerEntriesOrTheAuditTrail()
    {
        var offenders = SqlLiterals()
            .Where(literal => Mutated(literal.Sql).Intersect(ImmutableTables).Any())
            .Select(literal => $"{literal.Type}.{literal.Field}")
            .ToList();

        offenders.ShouldBeEmpty("These SQL literals change an immutable table: " + string.Join(", ", offenders));
    }

    [Fact]
    public void TheOnlyUpdatesOfTheOutbox_AreTheClaimTheMarkAndTheRelease()
    {
        FieldsThat("UPDATE", "outbox_messages").ShouldBe(OutboxUpdates, ignoreOrder: true);
    }

    [Fact]
    public void TheOnlyDeleteOfTheOutbox_IsThePrune()
    {
        FieldsThat("DELETE", "outbox_messages").ShouldBe(OutboxDeletes);
    }

    [Fact]
    public void TheOnlyUpdateOfTheAccounts_IsTheKeyRewrap()
    {
        FieldsThat("UPDATE", "accounts").ShouldBe(AccountUpdates);
    }

    private static List<string> FieldsThat(string verb, string table)
    {
        return SqlLiterals()
            .Where(literal => Statements(literal.Sql).Any(statement =>
                statement.Verb == verb && string.Equals(statement.Table, table, StringComparison.OrdinalIgnoreCase)))
            .Select(literal => literal.Field)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static HashSet<string> Mutated(string sql) =>
        new(Statements(sql).Select(statement => statement.Table), StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<(string Verb, string Table)> Statements(string sql)
    {
        foreach (Match match in Mutation().Matches(sql))
        {
            var verb = match.Groups["verb"].Value.ToUpperInvariant();

            yield return (verb.StartsWith("DELETE", StringComparison.Ordinal) ? "DELETE"
                : verb.StartsWith("TRUNCATE", StringComparison.Ordinal) ? "TRUNCATE"
                : "UPDATE", match.Groups["table"].Value);
        }
    }

    private static IEnumerable<(string Type, string Field, string Sql)> SqlLiterals()
    {
        foreach (var type in LayerAssemblies.Infrastructure.GetTypes())
        {
            foreach (var field in type.GetFields(AnyStaticField).Where(candidate => candidate.FieldType == typeof(string)))
            {
                var value = field.IsLiteral ? field.GetRawConstantValue() : field.GetValue(null);

                if (value is string sql && Mutation().IsMatch(sql))
                {
                    yield return (type.Name, field.Name, sql);
                }
            }
        }
    }

    [GeneratedRegex(
        @"\b(?<verb>UPDATE|DELETE\s+FROM|TRUNCATE(?:\s+TABLE)?)\s+(?:ONLY\s+)?(?<table>[a-z_][a-z0-9_]*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Mutation();
}
