using Ledger.Infrastructure.Persistence;
using Npgsql;

namespace Ledger.Infrastructure.Tests.Resilience;

[Trait("Category", "Unit")]
public sealed class PostgresDependencyFailureInspectorTests
{
    private readonly PostgresDependencyFailureInspector _inspector = new();

    private static PostgresException Postgres(string sqlState) =>
        new("failure", "ERROR", "ERROR", sqlState);

    [Theory]
    [InlineData(PostgresErrorCodes.DeadlockDetected)]
    [InlineData(PostgresErrorCodes.LockNotAvailable)]
    [InlineData(PostgresErrorCodes.ReadOnlySqlTransaction)]
    [InlineData(PostgresErrorCodes.CrashShutdown)]
    [InlineData(PostgresErrorCodes.DiskFull)]
    [InlineData(PostgresErrorCodes.UniqueViolation)]
    public void SqlState_OfADirectPostgresException_IsTheFiveCharacterCode(string sqlState)
    {
        _inspector.SqlState(Postgres(sqlState)).ShouldBe(sqlState);
    }

    [Fact]
    public void SqlState_OfAWrappedPostgresException_IsFound()
    {
        var wrapped = new InvalidOperationException("outer", new InvalidOperationException("middle", Postgres("57014")));

        _inspector.SqlState(wrapped).ShouldBe("57014");
    }

    [Fact]
    public void SqlState_OfAnAggregate_IsTheFirstCodeFound()
    {
        var aggregate = new AggregateException(
            new TimeoutException("first"),
            new InvalidOperationException("second", Postgres("55P03")),
            Postgres("40001"));

        _inspector.SqlState(aggregate).ShouldBe("55P03");
    }

    [Fact]
    public void SqlState_OfAnythingElse_IsNull()
    {
        _inspector.SqlState(new TimeoutException("slow")).ShouldBeNull();
        _inspector.SqlState(new InvalidOperationException("outer", new TimeoutException("inner"))).ShouldBeNull();
        _inspector.SqlState(new NpgsqlException("connection lost")).ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("4000")]
    [InlineData("400001")]
    public void SqlState_OfACodeWithoutFiveCharacters_IsNull(string sqlState)
    {
        _inspector.SqlState(Postgres(sqlState)).ShouldBeNull();
    }

    [Fact]
    public void SqlState_OfANullException_Throws()
    {
        Should.Throw<ArgumentNullException>(() => _inspector.SqlState(null!));
    }
}
