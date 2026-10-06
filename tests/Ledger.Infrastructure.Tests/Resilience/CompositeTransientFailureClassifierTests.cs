using Ledger.Application.Abstractions;
using Ledger.Application.Security;
using Ledger.Infrastructure.Persistence;
using Ledger.Infrastructure.Resilience;
using Npgsql;

namespace Ledger.Infrastructure.Tests.Resilience;

[Trait("Category", "Unit")]
public sealed class CompositeTransientFailureClassifierTests
{
    private static CompositeTransientFailureClassifier Composite() =>
        new(new PostgresTransientFailureClassifier(), new KeyProviderFailureClassifier());

    private static PostgresException Postgres(string sqlState) =>
        new("failure", "ERROR", "ERROR", sqlState);

    [Fact]
    public void IsTransient_KeyProviderUnavailable_IsTransient()
    {
        Composite().IsTransient(new KeyProviderUnavailableException()).ShouldBeTrue();
    }

    [Fact]
    public void IsTransient_KeyProviderUnavailableWrappedInAnInvalidOperation_IsTransient()
    {
        var wrapped = new InvalidOperationException("outer", new KeyProviderUnavailableException());

        Composite().IsTransient(wrapped).ShouldBeTrue();
    }

    [Fact]
    public void IsTransient_KeyProviderUnavailableTwoLevelsDown_IsTransient()
    {
        var wrapped = new AggregateException(new InvalidOperationException("middle", new KeyProviderUnavailableException()));

        Composite().IsTransient(wrapped).ShouldBeTrue();
    }

    [Fact]
    public void IsTransient_UniqueViolation_StaysNotTransient()
    {
        Composite().IsTransient(Postgres(PostgresErrorCodes.UniqueViolation)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(PostgresErrorCodes.DeadlockDetected)]
    [InlineData(PostgresErrorCodes.SerializationFailure)]
    [InlineData(PostgresErrorCodes.LockNotAvailable)]
    public void IsTransient_TransientDatabaseFailure_StillIsTransient(string sqlState)
    {
        Composite().IsTransient(Postgres(sqlState)).ShouldBeTrue();
    }

    [Fact]
    public void IsTransient_AnyOtherException_StaysNotTransient()
    {
        Composite().IsTransient(new InvalidOperationException("bug")).ShouldBeFalse();
        Composite().IsTransient(new ArgumentException("bad")).ShouldBeFalse();
    }

    [Fact]
    public void IsTransient_TimeoutException_FollowsTheDatabaseClassifier()
    {
        Composite().IsTransient(new TimeoutException()).ShouldBeTrue();
    }

    [Fact]
    public void KeyProviderFailureClassifier_OnlyRecognizesTheKeyOutage()
    {
        var classifier = new KeyProviderFailureClassifier();

        classifier.IsTransient(new KeyProviderUnavailableException()).ShouldBeTrue();
        classifier.IsTransient(Postgres(PostgresErrorCodes.DeadlockDetected)).ShouldBeFalse();
        classifier.IsTransient(new TimeoutException()).ShouldBeFalse();
    }
}
