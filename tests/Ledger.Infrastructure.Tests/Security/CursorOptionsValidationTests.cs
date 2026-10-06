using Ledger.Application.Abstractions;
using Ledger.Application.Tests.Security;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public sealed class CursorOptionsValidationTests
{
    private const string SigningKeyName = "Security:Cursor:SigningKey";
    private const string ValidKey = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=";

    private static ServiceProvider Build(string? signingKey, bool validateOnStart = true)
    {
        var values = new Dictionary<string, string?>();

        if (signingKey is not null)
        {
            values[SigningKeyName] = signingKey;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        return new ServiceCollection()
            .AddLedgerCursor(configuration, validateOnStart)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private static string Base64OfBytes(int length) => Convert.ToBase64String(Enumerable.Repeat((byte)7, length).ToArray());

    [Fact]
    public void Options_KeyOfThirtyTwoBytes_Passes()
    {
        using var provider = Build(ValidKey);

        provider.GetRequiredService<IOptions<CursorOptions>>().Value.SigningKey.ShouldBe(ValidKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("this is not base64 at all !!")]
    public void Options_AbsentEmptyOrNotBase64_RefusesNamingTheKey(string? signingKey)
    {
        using var provider = Build(signingKey);

        var failure = Should.Throw<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<CursorOptions>>().Value);

        failure.Message.ShouldContain(SigningKeyName, Case.Sensitive);
    }

    [Theory]
    [InlineData(31)]
    [InlineData(33)]
    public void Options_KeyOfTheWrongSize_RefusesNamingTheKeyAndNeverTheValue(int length)
    {
        var value = Base64OfBytes(length);
        using var provider = Build(value);

        var failure = Should.Throw<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<CursorOptions>>().Value);

        failure.Message.ShouldContain(SigningKeyName, Case.Sensitive);
        failure.Message.ShouldNotContain(value, Case.Sensitive);
        failure.ToString().ShouldNotContain(value, Case.Sensitive);
    }

    [Fact]
    public void Options_ValueThatIsNotBase64_IsNeverRepeatedInTheMessage()
    {
        const string value = "plainly-a-secret-that-is-not-base64!";
        using var provider = Build(value);

        var failure = Should.Throw<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<CursorOptions>>().Value);

        failure.Message.ShouldNotContain(value, Case.Sensitive);
    }

    [Fact]
    public void Protector_WithAValidKey_SignsAndVerifies()
    {
        using var provider = Build(ValidKey);
        var protector = provider.GetRequiredService<IStatementCursorProtector>();
        var account = AccountId.From(Guid.Parse(SecurityVectors.AccountText)).Value;
        var position = new StatementPosition(new DateTimeOffset(2026, 10, 1, 14, 3, 11, TimeSpan.Zero), 12);

        var restored = protector.Unprotect(account, protector.Protect(account, position));

        restored.IsSuccess.ShouldBeTrue();
        restored.Value.ShouldBe(position);
    }

    [Fact]
    public void Protector_WithoutTheKeyAndWithoutValidationOnStart_FailsOnlyWhenResolved()
    {
        using var provider = Build(null, validateOnStart: false);

        Should.Throw<OptionsValidationException>(() =>
            provider.GetRequiredService<IStatementCursorProtector>());
    }
}
