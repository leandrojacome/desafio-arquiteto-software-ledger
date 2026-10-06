using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Serilog.Events;

namespace Ledger.Api.IntegrationTests.Security;

[Trait("Category", "Integration")]
public sealed class DataProtectionTests
{
    private const string DataProtectionCategory = "Microsoft.AspNetCore.DataProtection";

    [Fact]
    public void TheKeyRing_IsHeldInMemoryByAnExplicitRepositoryAndEncryptor()
    {
        using var factory = TestApiFactory.With();

        var options = factory.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        options.XmlRepository.ShouldBeOfType<InMemoryKeyRepository>();
        options.XmlEncryptor.ShouldBeOfType<NullXmlEncryptor>();
    }

    [Fact]
    public void TheKeyRing_StillProtectsAndUnprotectsWithinTheProcess()
    {
        using var factory = TestApiFactory.With();
        var protector = factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("ledger-api-tests");

        var sealedText = protector.Protect("payload");

        sealedText.ShouldNotContain("payload");
        protector.Unprotect(sealedText).ShouldBe("payload");
    }

    [Fact]
    public void TheKeyRing_KeepsItsKeysInTheRepositoryAfterTheFirstUse()
    {
        using var factory = TestApiFactory.With();
        var protector = factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("ledger-api-tests");

        _ = protector.Protect("payload");

        factory.Services.GetRequiredService<InMemoryKeyRepository>().GetAllElements().ShouldNotBeEmpty();
    }

    [Fact]
    public void TheStartup_LogsNoWarningFromTheDataProtectionStack()
    {
        using var factory = TestApiFactory.With();

        _ = factory.Services;

        var warnings = factory.Sink.Events
            .Where(logEvent => logEvent.Level >= LogEventLevel.Warning)
            .Where(logEvent => SourceOf(logEvent).StartsWith(DataProtectionCategory, StringComparison.Ordinal))
            .Select(logEvent => logEvent.MessageTemplate.Text)
            .ToList();

        warnings.ShouldBeEmpty();
    }

    [Fact]
    public void TheStartup_LogsNoWarningAtAllInTheTestingEnvironment()
    {
        using var factory = TestApiFactory.With();

        _ = factory.Services;

        var warnings = factory.Sink.Events
            .Where(logEvent => logEvent.Level >= LogEventLevel.Warning)
            .Select(logEvent => $"{SourceOf(logEvent)}: {logEvent.MessageTemplate.Text}")
            .ToList();

        warnings.ShouldBeEmpty();
    }

    private static string SourceOf(LogEvent logEvent) =>
        logEvent.Properties.TryGetValue("SourceContext", out var value)
            ? value.ToString().Trim('"')
            : string.Empty;
}
