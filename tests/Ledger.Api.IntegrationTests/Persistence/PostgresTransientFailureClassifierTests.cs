using Ledger.Infrastructure.Persistence;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Persistence;

[Trait("Category", "Unit")]
public sealed class PostgresTransientFailureClassifierTests
{
    private const string Severity = "ERROR";

    public static TheoryData<string> ServiceUnavailableSqlStates => new()
    {
        PostgresErrorCodes.DeadlockDetected,
        PostgresErrorCodes.SerializationFailure,
        PostgresErrorCodes.LockNotAvailable,
        PostgresErrorCodes.QueryCanceled,
        PostgresErrorCodes.TooManyConnections,
        PostgresErrorCodes.AdminShutdown,
        PostgresErrorCodes.CannotConnectNow,
        PostgresErrorCodes.DiskFull,
        PostgresErrorCodes.ReadOnlySqlTransaction,
        PostgresErrorCodes.CrashShutdown,
        PostgresSqlStates.IdleInTransactionSessionTimeout
    };

    public static TheoryData<string> DefectSqlStates => new()
    {
        PostgresErrorCodes.UniqueViolation,
        PostgresErrorCodes.ForeignKeyViolation,
        PostgresErrorCodes.CheckViolation,
        PostgresErrorCodes.InFailedSqlTransaction,
        PostgresErrorCodes.UndefinedTable
    };

    [Theory]
    [MemberData(nameof(ServiceUnavailableSqlStates))]
    public void IsTransient_ForInfrastructureSqlStates_ReturnsTrue(string sqlState)
    {
        var classifier = new PostgresTransientFailureClassifier();

        classifier.IsTransient(new PostgresException("failure", Severity, Severity, sqlState)).ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(DefectSqlStates))]
    public void IsTransient_ForDefectSqlStates_ReturnsFalse(string sqlState)
    {
        var classifier = new PostgresTransientFailureClassifier();

        classifier.IsTransient(new PostgresException("failure", Severity, Severity, sqlState)).ShouldBeFalse();
    }
}
