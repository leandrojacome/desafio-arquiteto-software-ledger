namespace Ledger.Api.IntegrationTests.Infrastructure;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute()
        : this(Environment.GetEnvironmentVariable)
    {
    }

    internal DockerFactAttribute(Func<string, string?> variable)
    {
        Skip = DockerAvailability.SkipReason(variable);
    }
}

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
