namespace Ledger.Architecture.Tests.Hygiene;

[Trait("Category", "Architecture")]
public sealed class ReadSqlMatchesFlowPagesTests
{
    public static TheoryData<string, string> Statements() => new()
    {
        { "ReadCurrentBalanceSql", "consulta-de-saldo.md" },
        { "ReadBalanceAtSql", "consulta-em-um-instante.md" },
        { "ReadStatementPageSql", "extrato.md" },
        { "CheckHeadsSql", "conferencia-de-integridade.md" },
        { "CheckChainSql", "conferencia-de-integridade.md" },
        { "InsertAuditSql", "conferencia-de-integridade.md" }
    };

    [Theory]
    [MemberData(nameof(Statements))]
    public void TheConstant_IsInASqlBlockOfItsFlowPage(string constant, string page)
    {
        DocumentedSql.ShouldAppearInPage(constant, page);
    }

    [Theory]
    [InlineData("ReadCurrentBalanceSql")]
    [InlineData("ReadBalanceAtSql")]
    [InlineData("ReadStatementPageSql")]
    [InlineData("CheckHeadsSql")]
    [InlineData("CheckChainSql")]
    public void TheReadStatement_ListsItsColumnsAndNeverWritesAnything(string constant)
    {
        var sql = DocumentedSql.ConstantNamed(constant);

        sql.ShouldNotContain("SELECT *");
        sql.ShouldNotContain("INSERT ", Case.Insensitive);
        sql.ShouldNotContain("UPDATE ", Case.Insensitive);
        sql.ShouldNotContain("DELETE ", Case.Insensitive);
    }
}
