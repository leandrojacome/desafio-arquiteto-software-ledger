using Ledger.Architecture.Tests.Ci;

namespace Ledger.Architecture.Tests.Layers;

[Trait("Category", "Architecture")]
public sealed class TestProjectDependencyTests
{
    private static readonly string[] UnitTestProjects =
    [
        "Ledger.Domain.Tests",
        "Ledger.Application.Tests",
        "Ledger.Infrastructure.Tests"
    ];

    [Theory]
    [InlineData("Ledger.Domain.Tests", "Ledger.Application")]
    [InlineData("Ledger.Domain.Tests", "Ledger.Infrastructure")]
    [InlineData("Ledger.Application.Tests", "Ledger.Infrastructure")]
    [InlineData("Ledger.Application.Tests", "Ledger.Api")]
    [InlineData("Ledger.Application.Tests", "Ledger.Worker")]
    public void AUnitTestProject_DoesNotReferenceALayerAboveWhatItTests(string testProject, string forbiddenProject)
    {
        var references = ProjectReferences(testProject);

        references.ShouldNotContain(forbiddenProject);
    }

    [Fact]
    public void TheInfrastructureTests_ReferenceTheInfrastructure()
    {
        ProjectReferences("Ledger.Infrastructure.Tests").ShouldContain("Ledger.Infrastructure");
    }

    [Fact]
    public void TheUnitTestProjects_AreAllInTheSolution()
    {
        var solution = RepositoryFiles.ReadAllText("Ledger.sln");

        foreach (var project in UnitTestProjects)
        {
            solution.ShouldContain($"tests\\{project}\\{project}.csproj");
        }
    }

    private static List<string> ProjectReferences(string testProject)
    {
        var project = RepositoryFiles.ReadAllText($"tests/{testProject}/{testProject}.csproj");

        return project
            .Split('\n')
            .Where(line => line.Contains("<ProjectReference", StringComparison.Ordinal))
            .Select(line => Path.GetFileNameWithoutExtension(line.Split('"')[1].Replace('\\', '/')))
            .ToList();
    }
}
