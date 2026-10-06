namespace Ledger.Api.IntegrationTests.Infrastructure;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class DockerTheoryAttribute : TheoryAttribute
{
    public DockerTheoryAttribute()
        : this(Environment.GetEnvironmentVariable)
    {
    }

    internal DockerTheoryAttribute(Func<string, string?> variable)
    {
        Skip = DockerAvailability.SkipReason(variable);
    }
}
