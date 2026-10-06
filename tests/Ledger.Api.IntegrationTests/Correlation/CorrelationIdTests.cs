using System.Net;
using System.Text.RegularExpressions;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Security;

namespace Ledger.Api.IntegrationTests.Correlation;

[Trait("Category", "Integration")]
public sealed partial class CorrelationIdTests
{
    private const string Header = "X-Correlation-Id";
    private const string RejectionLog = "A received X-Correlation-Id was discarded";

    public static TheoryData<string> RejectedValues => new()
    {
        "short",
        "contains spaces in the value",
        "has\u0001control-characters",
        "0123456789012345678901234567890123456789012345678901234567890123456789"
    };

    [Fact]
    public async Task AValidValue_ComesBackUnchanged()
    {
        using var factory = TestApiFactory.With();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, TestEndpointsStartupFilter.Open);
        request.Headers.Add(Header, "9f3c1a7e2b4d4f60a1c8e5d7b3a29f10");

        using var response = await client.SendAsync(request, CancellationToken.None);

        response.Headers.GetValues(Header).ShouldHaveSingleItem().ShouldBe("9f3c1a7e2b4d4f60a1c8e5d7b3a29f10");
        RejectionLogged(factory).ShouldBeFalse();
    }

    [Fact]
    public async Task AMissingValue_IsGeneratedAsThirtyTwoHexadecimalsWithoutAWarning()
    {
        using var factory = TestApiFactory.With();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(TestEndpointsStartupFilter.Open, CancellationToken.None);

        GeneratedValue().IsMatch(response.Headers.GetValues(Header).ShouldHaveSingleItem()).ShouldBeTrue();
        RejectionLogged(factory).ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(RejectedValues))]
    public async Task ARejectedValue_IsReplacedAndNeverReachesTheLogs(string received)
    {
        using var factory = TestApiFactory.With();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, TestEndpointsStartupFilter.Open);
        request.Headers.TryAddWithoutValidation(Header, received);

        using var response = await client.SendAsync(request, CancellationToken.None);

        var returned = response.Headers.GetValues(Header).ShouldHaveSingleItem();

        returned.ShouldNotBe(received);
        GeneratedValue().IsMatch(returned).ShouldBeTrue();
        RejectionLogged(factory).ShouldBeTrue();

        if (received.Length > 0)
        {
            factory.Sink.Everything().ShouldNotContain(received);
        }
    }

    [Fact]
    public async Task MoreThanOneValue_IsReplaced()
    {
        using var factory = TestApiFactory.With();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, TestEndpointsStartupFilter.Open);
        request.Headers.TryAddWithoutValidation(Header, ["first-value-0001", "second-value-0002"]);

        using var response = await client.SendAsync(request, CancellationToken.None);

        var returned = response.Headers.GetValues(Header).ShouldHaveSingleItem();

        GeneratedValue().IsMatch(returned).ShouldBeTrue();
        RejectionLogged(factory).ShouldBeTrue();
    }

    [Theory]
    [InlineData("/__test/open", 200)]
    [InlineData("/__test/missing", 404)]
    [InlineData("/__test/boom", 500)]
    [InlineData("/__test/transient", 503)]
    public async Task TheHeader_IsPresentWhateverTheStatus(string route, int status)
    {
        using var factory = TestApiFactory.With();
        using var client = factory.ClientWith(TokenForge.Hmac());

        using var response = await client.GetAsync(route, CancellationToken.None);

        ((int)response.StatusCode).ShouldBe(status);
        response.Headers.Contains(Header).ShouldBeTrue();
    }

    [Fact]
    public async Task TheHeader_IsPresentOnAMethodNotAllowed()
    {
        using var factory = TestApiFactory.With();
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/health/live", content: null, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        response.Headers.Contains(Header).ShouldBeTrue();
    }

    [GeneratedRegex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex GeneratedValue();

    private static bool RejectionLogged(TestApiFactory factory)
    {
        return factory.Sink.Events.Any(logEvent =>
            logEvent.MessageTemplate.Text.StartsWith(RejectionLog, StringComparison.Ordinal));
    }
}
