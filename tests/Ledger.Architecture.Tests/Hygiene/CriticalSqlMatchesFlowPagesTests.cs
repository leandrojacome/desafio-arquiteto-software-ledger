namespace Ledger.Architecture.Tests.Hygiene;

[Trait("Category", "Architecture")]
public sealed class CriticalSqlMatchesFlowPagesTests
{
    public static TheoryData<string, string> Statements() => new()
    {
        { "ReserveKeySql", "registro-de-lancamento.md" },
        { "ApplyEntrySql", "registro-de-lancamento.md" },
        { "EnqueueEventSql", "registro-de-lancamento.md" },
        { "DiagnoseRefusalSql", "registro-de-lancamento.md" },
        { "ReadReplaySql", "repeticao-idempotente.md" },
        { "ReadReversalCandidateSql", "estorno.md" },
        { "CreateAccountSql", "criacao-de-conta.md" },
        { "ReserveCreationKeySql", "criacao-de-conta.md" },
        { "ReadCreationKeySql", "criacao-de-conta.md" },
        { "InsertAuditSql", "criacao-de-conta.md" },
        { "ClaimOutboxBatchSql", "publicacao-do-outbox.md" },
        { "MarkOutboxPublishedSql", "publicacao-do-outbox.md" },
        { "ReleaseOutboxSql", "publicacao-do-outbox.md" },
        { "PruneOutboxSql", "rotinas-de-manutencao.md" }
    };

    [Theory]
    [MemberData(nameof(Statements))]
    public void TheConstant_IsInASqlBlockOfItsFlowPage(string constant, string page)
    {
        DocumentedSql.ShouldAppearInPage(constant, page);
    }

    [Theory]
    [InlineData("ReserveKeySql")]
    [InlineData("ApplyEntrySql")]
    [InlineData("EnqueueEventSql")]
    [InlineData("ReadReplaySql")]
    [InlineData("DiagnoseRefusalSql")]
    [InlineData("ReadReversalCandidateSql")]
    [InlineData("CreateAccountSql")]
    [InlineData("InsertAuditSql")]
    [InlineData("ClaimOutboxBatchSql")]
    [InlineData("MarkOutboxPublishedSql")]
    [InlineData("PruneOutboxSql")]
    public void TheConstant_TakesItsValuesAsNamedParameters(string constant)
    {
        DocumentedSql.ConstantNamed(constant).ShouldContain("@");
    }

    [Theory]
    [InlineData("ClaimOutboxBatchSql")]
    [InlineData("PruneOutboxSql")]
    public void TheOutboxIntervals_ComeFromTheConfigurationThroughMakeInterval(string constant)
    {
        var sql = DocumentedSql.ConstantNamed(constant);

        sql.ShouldContain("make_interval(");
        sql.ShouldNotContain("INTERVAL '", Case.Insensitive);
    }

    [Fact]
    public void TheComparison_NoticesAChangeOfTheStatement()
    {
        var original = DocumentedSql.ConstantNamed("ReserveKeySql");
        var changed = original.Replace("DO NOTHING", "DO UPDATE SET request_hash = excluded.request_hash", StringComparison.Ordinal);

        DocumentedSql.AppearsInPage(original, "registro-de-lancamento.md").ShouldBeTrue();
        DocumentedSql.AppearsInPage(changed, "registro-de-lancamento.md").ShouldBeFalse();
        DocumentedSql.AppearsInPage(original, "consulta-de-saldo.md").ShouldBeFalse();
    }
}
