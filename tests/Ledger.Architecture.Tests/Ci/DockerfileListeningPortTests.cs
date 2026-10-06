using System.Text.RegularExpressions;

namespace Ledger.Architecture.Tests.Ci;

[Trait("Category", "Unit")]
public sealed partial class DockerfileListeningPortTests
{
    [Theory]
    [InlineData("docker/Dockerfile.api", "8080")]
    [InlineData("docker/Dockerfile.worker", "8081")]
    public void TheImage_ChoosesItsPortWithAspNetCoreHttpPorts(string dockerfile, string port)
    {
        var text = RepositoryFiles.ReadAllText(dockerfile);

        HttpPorts().Match(text).Groups["port"].Value.ShouldBe(port);
    }

    [Theory]
    [InlineData("docker/Dockerfile.api")]
    [InlineData("docker/Dockerfile.worker")]
    public void TheImage_NeverDefinesAspNetCoreUrlsBecauseTheBaseImageAlreadyDefinesHttpPorts(string dockerfile)
    {
        var text = RepositoryFiles.ReadAllText(dockerfile);

        text.ShouldNotContain("ASPNETCORE_URLS", Case.Sensitive);
    }

    [GeneratedRegex(@"ASPNETCORE_HTTP_PORTS=(?<port>\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex HttpPorts();
}
