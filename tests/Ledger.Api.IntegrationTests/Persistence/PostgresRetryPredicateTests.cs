using Ledger.Infrastructure.Persistence;
using Ledger.Infrastructure.Persistence.Retry;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Persistence;

[Trait("Category", "Unit")]
public sealed class PostgresRetryPredicateTests
{
    private const string Severity = "ERROR";

    public static TheoryData<string> RetryableSqlStates => new()
    {
        PostgresErrorCodes.DeadlockDetected,
        PostgresErrorCodes.SerializationFailure,
        PostgresErrorCodes.ConnectionException,
        PostgresErrorCodes.ConnectionFailure,
        PostgresErrorCodes.ConnectionDoesNotExist,
        PostgresErrorCodes.SqlClientUnableToEstablishSqlConnection,
        PostgresErrorCodes.AdminShutdown,
        PostgresErrorCodes.CannotConnectNow,
        PostgresSqlStates.IdleInTransactionSessionTimeout
    };

    public static TheoryData<string> FinalSqlStates => new()
    {
        PostgresErrorCodes.LockNotAvailable,
        PostgresErrorCodes.QueryCanceled,
        PostgresErrorCodes.TooManyConnections,
        PostgresErrorCodes.DiskFull,
        PostgresErrorCodes.CrashShutdown,
        PostgresErrorCodes.ReadOnlySqlTransaction,
        PostgresErrorCodes.InFailedSqlTransaction,
        PostgresErrorCodes.UniqueViolation,
        PostgresErrorCodes.ForeignKeyViolation,
        PostgresErrorCodes.CheckViolation,
        PostgresErrorCodes.IntegrityConstraintViolation
    };

    [Theory]
    [MemberData(nameof(RetryableSqlStates))]
    public void ShouldRetry_ForTransientSqlStates_ReturnsTrue(string sqlState)
    {
        PostgresRetryPredicate.ShouldRetry(PostgresFailure(sqlState)).ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(FinalSqlStates))]
    public void ShouldRetry_ForStatesWhereRepeatingDoesNotHelp_ReturnsFalse(string sqlState)
    {
        PostgresRetryPredicate.ShouldRetry(PostgresFailure(sqlState)).ShouldBeFalse();
    }

    [Fact]
    public void ShouldRetry_ForATransientNpgsqlExceptionWithoutATimeout_ReturnsTrue()
    {
        var exception = new NpgsqlException("connection lost", new IOException("broken pipe"));

        exception.IsTransient.ShouldBeTrue();
        PostgresRetryPredicate.ShouldRetry(exception).ShouldBeTrue();
    }

    [Fact]
    public void ShouldRetry_ForANpgsqlExceptionWrappingATimeout_ReturnsFalse()
    {
        var exception = new NpgsqlException("pool exhausted", new TimeoutException());

        exception.IsTransient.ShouldBeTrue();
        PostgresRetryPredicate.ShouldRetry(exception).ShouldBeFalse();
    }

    [Fact]
    public void ShouldRetry_ForATimeoutDeepInsideTheChain_ReturnsFalse()
    {
        var exception = new NpgsqlException("outer", new IOException("middle", new TimeoutException()));

        PostgresRetryPredicate.ShouldRetry(exception).ShouldBeFalse();
    }

    [Fact]
    public void ShouldRetry_ForANonTransientNpgsqlException_ReturnsFalse()
    {
        PostgresRetryPredicate.ShouldRetry(new NpgsqlException("bad protocol")).ShouldBeFalse();
    }

    [Fact]
    public void ShouldRetry_ForExceptionsThatAreNotFromTheDatabase_ReturnsFalse()
    {
        PostgresRetryPredicate.ShouldRetry(new InvalidOperationException()).ShouldBeFalse();
        PostgresRetryPredicate.ShouldRetry(new OperationCanceledException()).ShouldBeFalse();
        PostgresRetryPredicate.ShouldRetry(new TimeoutException()).ShouldBeFalse();
    }

    private static PostgresException PostgresFailure(string sqlState) =>
        new("failure", Severity, Severity, sqlState);
}
