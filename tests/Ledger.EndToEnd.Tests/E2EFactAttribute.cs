namespace Ledger.EndToEnd.Tests;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class E2EFactAttribute : FactAttribute
{
    public E2EFactAttribute()
        : this(Environment.GetEnvironmentVariable)
    {
    }

    internal E2EFactAttribute(Func<string, string?> variable)
    {
        Skip = E2EEnvironment.SkipReason(variable);
    }
}
