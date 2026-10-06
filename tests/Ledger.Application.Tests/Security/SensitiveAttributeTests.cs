using System.Reflection;
using Ledger.Application.Security;

namespace Ledger.Application.Tests.Security;

[Trait("Category", "Unit")]
public sealed class SensitiveAttributeTests
{
    private static AttributeUsageAttribute Usage { get; } = typeof(SensitiveAttribute).GetCustomAttribute<AttributeUsageAttribute>()
        .ShouldNotBeNull();

    [Fact]
    public void TheAttribute_IsSealed()
    {
        typeof(SensitiveAttribute).IsSealed.ShouldBeTrue();
    }

    [Fact]
    public void TheAttribute_TakesNoArguments()
    {
        var constructors = typeof(SensitiveAttribute).GetConstructors();

        constructors.Length.ShouldBe(1);
        constructors[0].GetParameters().ShouldBeEmpty();
        typeof(SensitiveAttribute).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).ShouldBeEmpty();
    }

    [Fact]
    public void TheAttribute_MarksPropertiesFieldsAndParametersOnly()
    {
        Usage.ValidOn.ShouldBe(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter);
    }

    [Fact]
    public void TheAttribute_CannotBeRepeatedOnTheSameMember()
    {
        Usage.AllowMultiple.ShouldBeFalse();
    }
}
