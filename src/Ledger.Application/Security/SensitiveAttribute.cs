namespace Ledger.Application.Security;

[AttributeUsage(
    AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter,
    AllowMultiple = false,
    Inherited = true)]
public sealed class SensitiveAttribute : Attribute
{
}
