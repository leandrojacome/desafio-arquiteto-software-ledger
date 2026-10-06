using System.Globalization;
using System.Text.RegularExpressions;

namespace Ledger.Architecture.Tests.Ci;

[Trait("Category", "Architecture")]
public sealed partial class CiPipelineTests
{
    private static readonly string[] StageNames = ["validate", "test", "package", "registryCheck", "release"];

    private static readonly string[] HostSpecificTokens =
    [
        "Build.Repository.Provider",
        "System.PullRequest.",
        "type: github",
        "type: bitbucket",
        "GitHubRelease@",
        "GitHubComment@",
        "resources:"
    ];

    [Fact]
    public void TheEntryFileAndEveryTemplateExist()
    {
        RepositoryFiles.Exists(PipelineLoader.EntryFile).ShouldBeTrue();

        PipelineLoader.ReferencedTemplates().ShouldAllBe(template => RepositoryFiles.Exists(template));
    }

    [Fact]
    public void EveryTemplateFileIsReferencedByThePipeline()
    {
        PipelineLoader.TemplateFiles().ShouldBe(PipelineLoader.ReferencedTemplates());
    }

    [Fact]
    public void Stages_AreInTheExpectedOrder()
    {
        PipelineLoader.Stages().Select(stage => stage.Name).ShouldBe(StageNames);
    }

    [Theory]
    [InlineData("validate", "build,secrets")]
    [InlineData("test", "unit,integration,contract,e2e")]
    [InlineData("package", "images")]
    [InlineData("registryCheck", "check")]
    [InlineData("release", "push")]
    public void EachStage_HoldsTheExpectedJobs(string stage, string expected)
    {
        PipelineLoader.Stage(stage).Jobs.Select(job => job.Name).ShouldBe(expected.Split(','), ignoreOrder: true);
    }

    [Theory]
    [InlineData("validate", "")]
    [InlineData("test", "validate")]
    [InlineData("package", "validate")]
    [InlineData("registryCheck", "test,package")]
    [InlineData("release", "registryCheck")]
    public void EachStageDeclaresItsDependenciesInsteadOfRelyingOnTheOrder(string stage, string expected)
    {
        PipelineLoader.Stage(stage).DependsOn
            .ShouldBe(expected.Split(',', StringSplitOptions.RemoveEmptyEntries), ignoreOrder: true);
    }

    [Fact]
    public void StageDependenciesPointToExistingStagesAndHaveNoCycle()
    {
        var stages = PipelineLoader.Stages().ToDictionary(stage => stage.Name);
        var graph = stages.ToDictionary(pair => pair.Key, pair => pair.Value.DependsOn);

        foreach (var stage in stages.Values)
        {
            stage.DependsOn.ShouldAllBe(dependency => stages.ContainsKey(dependency) && dependency != stage.Name);
            Reaches(graph, stage.Name, stage.Name, []).ShouldBeFalse($"{stage.Name} depends on itself through a cycle.");
        }
    }

    [Fact]
    public void JobNamesAreUniqueAndOnlyTheEndToEndJobWaitsForOtherJobs()
    {
        PipelineLoader.AllJobs().Select(job => job.Name).ShouldBeUnique();

        var waiting = PipelineLoader.AllJobs().Where(job => job.DependsOn.Count > 0).ToList();

        waiting.Select(job => job.Name).ShouldBe(["e2e"]);
        waiting[0].DependsOn.ShouldBe(["unit", "integration", "contract"], ignoreOrder: true);
    }

    [Fact]
    public void TheMainBranchIsDeclaredOnceAsAVariable()
    {
        PipelineLoader.RootVariables()["mainBranch"].ShouldBe("refs/heads/main");
    }

    [Theory]
    [InlineData("registryCheck")]
    [InlineData("release")]
    public void TheRegistryStagesRunOnlyOnTheMainBranchOutsidePullRequestsAndOnlyWhenThePushIsOn(string stage)
    {
        var condition = PipelineLoader.Stage(stage).Condition;

        condition.ShouldNotBeNull();
        condition.ShouldContain("succeeded()");
        condition.ShouldContain("ne(variables['Build.Reason'], 'PullRequest')");
        condition.ShouldContain("eq(variables['Build.SourceBranch'], variables['mainBranch'])");
        condition.ShouldContain("parameters.pushImages");
        condition.ShouldContain("variables['LEDGER_PUSH_IMAGES']");
    }

    [Theory]
    [InlineData("build", 25)]
    [InlineData("secrets", 15)]
    [InlineData("unit", 20)]
    [InlineData("integration", 60)]
    [InlineData("contract", 15)]
    [InlineData("e2e", 60)]
    [InlineData("images", 25)]
    [InlineData("check", 5)]
    [InlineData("push", 40)]
    public void EveryJobKeepsAtLeastTheTimeoutItsExpectedDurationNeeds(string name, int minimumMinutes)
    {
        var timeout = PipelineLoader.Job(name).TimeoutInMinutes;

        timeout.ShouldNotBeNull($"{name} has no timeoutInMinutes.");
        timeout.Value.ShouldBeGreaterThanOrEqualTo(minimumMinutes);
    }

    [Fact]
    public void EveryJobThatIsNotADeploymentStartsWithACheckoutThatIsShallowOrAbsent()
    {
        foreach (var job in PipelineLoader.AllJobs().Where(job => !job.IsDeployment))
        {
            var first = job.Steps[0];

            first.IsCheckout.ShouldBeTrue($"{job.Name} does not start with a checkout.");

            if (first.CheckoutTarget == "self")
            {
                first.FetchDepth.ShouldBe("1", $"{job.Name} does not check out with fetchDepth 1.");
            }
            else
            {
                first.CheckoutTarget.ShouldBe("none");
            }
        }
    }

    [Fact]
    public void TheDeploymentChecksOutShallowlyBecauseDeploymentsDoNotCheckOutByThemselves()
    {
        var checkout = PipelineLoader.Job("push").Steps.Single(step => step.IsCheckout);

        checkout.CheckoutTarget.ShouldBe("self");
        checkout.FetchDepth.ShouldBe("1");
    }

    [Fact]
    public void EveryStepThatRunsCommandsRunsThemDirectlyInBash()
    {
        var steps = PipelineLoader.AllJobs().SelectMany(job => job.Steps).Where(step => step.Script is not null).ToList();

        steps.ShouldNotBeEmpty();
        steps.ShouldAllBe(step => step.ScriptKind == "bash" && !step.Script!.Contains(".ps1", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryMultiLineBashStepStopsAtTheFirstFailure()
    {
        var offenders = PipelineLoader.AllJobs()
            .SelectMany(job => job.Steps.Select(step => (job.Name, step)))
            .Where(entry => entry.step is { Script: { } script }
                            && script.Trim().Contains('\n', StringComparison.Ordinal)
                            && !StrictShell().IsMatch(string.Join('\n', script.Split('\n').Take(2))))
            .Select(entry => $"{entry.Name}: {entry.step.DisplayName}")
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void BuildCompilesChecksTheFormattingAndLooksForVulnerablePackagesInThatOrder()
    {
        var scripts = PipelineLoader.Scripts(PipelineLoader.Job("build")).ToList();

        string[] expectedOrder =
        [
            "dotnet build -c Release",
            "dotnet format --verify-no-changes",
            "dotnet list package --vulnerable --include-transitive"
        ];

        var positions = expectedOrder.Select(fragment =>
            scripts.FindIndex(script => script.Contains(fragment, StringComparison.Ordinal))).ToList();

        positions.ShouldAllBe(position => position >= 0);
        positions.ShouldBe(positions.Order().ToList());
    }

    [Fact]
    public void TheVulnerabilityCheckFailsWhenTheCliReportsVulnerablePackages()
    {
        var script = PipelineLoader.Scripts(PipelineLoader.Job("build"))
            .Single(text => text.Contains("--vulnerable", StringComparison.Ordinal));

        script.ShouldContain("has the following vulnerable packages");
        script.ShouldContain("exit 1");
    }

    [Fact]
    public void SecretsAreScannedByTheTreeOfFilesWithThePinnedGitleaksImage()
    {
        var scripts = PipelineLoader.Scripts(PipelineLoader.Job("secrets")).ToList();

        scripts.ShouldContain(script =>
            script.Contains("$(gitleaksImage)", StringComparison.Ordinal)
            && script.Contains("detect --no-git", StringComparison.Ordinal)
            && script.Contains("--exit-code 1", StringComparison.Ordinal));
    }

    [Fact]
    public void UnitRunsTheSuitesThatNeedNoDockerThenTheCoverageFloorsAndPublishesResultsAndCoverage()
    {
        var job = PipelineLoader.Job("unit");
        var scripts = PipelineLoader.Scripts(job).ToList();

        scripts.ShouldContain(script =>
            script.Contains("FullyQualifiedName!~IntegrationTests", StringComparison.Ordinal)
            && script.Contains("FullyQualifiedName!~EndToEnd", StringComparison.Ordinal));
        scripts.ShouldContain(script =>
            script.Contains("tests/Ledger.Domain.Tests", StringComparison.Ordinal)
            && script.Contains("-p:Threshold=95%2C90", StringComparison.Ordinal)
            && script.Contains("-p:ThresholdType=line%2Cbranch", StringComparison.Ordinal));
        scripts.ShouldContain(script =>
            script.Contains("tests/Ledger.Application.Tests", StringComparison.Ordinal)
            && script.Contains("-p:Threshold=90%2C80", StringComparison.Ordinal));
        job.Steps.Select(step => step.Task).ShouldContain("PublishTestResults@2");
        job.Steps.Select(step => step.Task).ShouldContain("PublishCodeCoverageResults@2");
        ArtifactsOf(job).ShouldContain("coverage");
    }

    [Fact]
    public void IntegrationHasAQuickProfileThatSkipsTheSlowTestsAndAFullProfileThatRunsEverything()
    {
        var scripts = PipelineLoader.Scripts(PipelineLoader.Job("integration")).ToList();

        scripts.ShouldAllBe(script => script.Contains("dotnet test tests/Ledger.Api.IntegrationTests", StringComparison.Ordinal));
        scripts.ShouldContain(script => script.Contains("--filter \"Speed!=Slow\"", StringComparison.Ordinal));
        scripts.ShouldContain(script => !script.Contains("--filter", StringComparison.Ordinal));
    }

    [Fact]
    public void ContractComparesTheGeneratedOpenApiDocumentWithTheVersionedOneAndPublishesIt()
    {
        var job = PipelineLoader.Job("contract");

        PipelineLoader.Scripts(job)
            .ShouldContain(script => script.Contains("FullyQualifiedName~OpenApiContractTests", StringComparison.Ordinal));
        job.Steps.Single(step => step.Task == "PublishPipelineArtifact@1").Inputs["targetPath"]
            .ShouldEndWith("docs/05-contratos/openapi.v1.json");
        ArtifactsOf(job).ShouldBe(["openapi"]);
    }

    [Fact]
    public void EndToEndProvisionsItsOwnStackWithALatencyFactorForTheSharedRunner()
    {
        var job = PipelineLoader.Job("e2e");

        PipelineLoader.Scripts(job).ShouldAllBe(script =>
            script.Contains("LEDGER_E2E_PROVISION=true dotnet test tests/Ledger.EndToEnd.Tests", StringComparison.Ordinal));
        PipelineLoader.Scripts(job).ShouldContain(script => !script.Contains("--filter", StringComparison.Ordinal));
        PipelineLoader.Scripts(job).ShouldContain(script => script.Contains("Category!=Resilience", StringComparison.Ordinal));
        double.Parse(job.Variables["LEDGER_E2E_LATENCY_FACTOR"], CultureInfo.InvariantCulture).ShouldBeGreaterThan(1);
    }

    [Theory]
    [InlineData("unit")]
    [InlineData("integration")]
    [InlineData("contract")]
    [InlineData("e2e")]
    public void EveryJobThatRunsTestsPublishesTheirResultsEvenWhenTheyFail(string name)
    {
        var publish = PipelineLoader.Job(name).Steps.Single(step => step.Task == "PublishTestResults@2");

        publish.Condition.ShouldBe("succeededOrFailed()");
        publish.Inputs["testResultsFormat"].ShouldBe("VSTest");
    }

    [Fact]
    public void EveryJobThatNeedsDockerInheritsTheVariableThatMakesItFailInsteadOfSkipping()
    {
        PipelineLoader.RootVariables()["LEDGER_REQUIRE_DOCKER"].ShouldBe("true");
        PipelineLoader.AllJobs().ShouldAllBe(job =>
            !job.Variables.ContainsKey("LEDGER_REQUIRE_DOCKER") || job.Variables["LEDGER_REQUIRE_DOCKER"] == "true");
    }

    [Fact]
    public void ImagesAreBuiltFromTheExampleEnvironmentFileScannedAndInventoriedWithoutBeingPushed()
    {
        var job = PipelineLoader.Job("images");
        var scripts = PipelineLoader.Scripts(job).ToList();

        scripts.ShouldContain("cp .env.example .env");
        scripts.ShouldContain(script => script.Contains("docker compose build", StringComparison.Ordinal));
        scripts.ShouldContain(script =>
            script.Contains("$(trivyImage)", StringComparison.Ordinal)
            && script.Contains("--severity HIGH,CRITICAL", StringComparison.Ordinal)
            && script.Contains("--exit-code 1", StringComparison.Ordinal));
        scripts.ShouldContain(script => script.Contains("--format cyclonedx", StringComparison.Ordinal));
        ArtifactsOf(job).ShouldBe(["sbom"]);
    }

    [Fact]
    public void NoJobOutsideTheReleaseStageLogsInToOrPushesToARegistry()
    {
        foreach (var job in PipelineLoader.AllJobs().Where(job => job.Name != "push"))
        {
            job.Steps.Any(step => step.Task == "Docker@2" || step.Script?.Contains("docker push", StringComparison.Ordinal) == true)
                .ShouldBeFalse($"{job.Name} reaches a registry before every gate had its say.");
        }
    }

    [Fact]
    public void TheDeploymentRunsInAnEnvironmentThatTheParameterNamesSoThatTheApprovalLivesThere()
    {
        var job = PipelineLoader.Job("push");

        job.IsDeployment.ShouldBeTrue();
        job.Environment.ShouldBe("${{ parameters.environment }}");
        RepositoryFiles.ReadAllText(PipelineLoader.EntryFile).ShouldContain("environment: ${{ parameters.releaseEnvironment }}");
    }

    [Fact]
    public void TheDeploymentRebuildsScansLogsInPushesAndLogsOutInThatOrder()
    {
        var job = PipelineLoader.Job("push");
        var steps = job.Steps.ToList();

        var positions = new[]
        {
            steps.FindIndex(step => step.Script?.Contains("docker compose build", StringComparison.Ordinal) == true),
            steps.FindIndex(step => step.Script?.Contains("$(trivyImage)", StringComparison.Ordinal) == true),
            steps.FindIndex(step => step.Task == "Docker@2" && step.Inputs["command"] == "login"),
            steps.FindIndex(step => step.Script?.Contains("docker push", StringComparison.Ordinal) == true),
            steps.FindIndex(step => step.Task == "Docker@2" && step.Inputs["command"] == "logout")
        };

        positions.ShouldAllBe(position => position >= 0);
        positions.ShouldBe(positions.Order().ToArray());
        job.Steps[positions[4]].Condition.ShouldBe("always()");
    }

    [Fact]
    public void TheImageTagIsTheCommitAndNeverLatest()
    {
        var job = PipelineLoader.Job("push");
        var build = job.Steps.Single(step => step.Script?.Contains("docker compose build", StringComparison.Ordinal) == true);
        var push = job.Steps.Single(step => step.Script?.Contains("docker push", StringComparison.Ordinal) == true);

        build.Env["LEDGER_IMAGE_TAG"].ShouldBe("$(Build.SourceVersion)");
        push.Script.ShouldNotBeNull();
        push.Script.ShouldNotContain(":latest");
        push.Script.ShouldContain("$(Build.SourceVersion)");
    }

    [Fact]
    public void ThePushIsRefusedBeforeTheApprovalWhenThereIsNoRegistryOrServiceConnection()
    {
        var guard = PipelineLoader.Job("check").Steps.Single(step => step.Script is not null);

        guard.Script.ShouldNotBeNull();
        guard.Script.ShouldContain("exit 1");
        guard.Env.Keys.ShouldBe(["REGISTRY", "CONNECTION"], ignoreOrder: true);
        guard.Env["REGISTRY"].ShouldContain("LEDGER_CONTAINER_REGISTRY");
        guard.Env["CONNECTION"].ShouldContain("LEDGER_REGISTRY_CONNECTION");
    }

    [Fact]
    public void TasksArePinnedByMajorVersion()
    {
        var tasks = PipelineLoader.AllJobs().SelectMany(job => job.Steps).Select(step => step.Task).OfType<string>()
            .Distinct(StringComparer.Ordinal).ToList();

        tasks.ShouldNotBeEmpty();
        tasks.ShouldAllBe(task => PinnedTask().IsMatch(task));
    }

    [Fact]
    public void ToolImagesCarryAVersionAndADigestAndNeverLatest()
    {
        var variables = PipelineLoader.RootVariables();

        variables["gitleaksImage"].ShouldMatch(@":v?\d+\.\d+\.\d+@sha256:[0-9a-f]{64}$");
        variables["trivyImage"].ShouldMatch(@":v?\d+\.\d+\.\d+@sha256:[0-9a-f]{64}$");
    }

    [Theory]
    [InlineData("docker/Dockerfile.api")]
    [InlineData("docker/Dockerfile.worker")]
    [InlineData("docker/Dockerfile.k6")]
    [InlineData("docker/postgres/Dockerfile")]
    public void TheBaseImagesOfTheDockerfilesArePinnedByDigest(string dockerfile)
    {
        var bases = RepositoryFiles.ReadAllText(dockerfile)
            .Split('\n')
            .Where(line => line.StartsWith("FROM ", StringComparison.Ordinal))
            .ToList();

        bases.ShouldNotBeEmpty();
        bases.ShouldAllBe(line => FromWithDigest().IsMatch(line));
    }

    [Fact]
    public void TheBrokerImageOfTheComposeFileIsPinnedByDigest()
    {
        RepositoryFiles.ReadAllText("docker-compose.yml").ShouldMatch(@"image: rabbitmq:[\w.\-]+@sha256:[0-9a-f]{64}");
    }

    [Fact]
    public void EveryParameterOfTheEntryFileHasATypeAndADefaultAndTheImagePushIsOffByDefault()
    {
        var parameters = ((List<object>)PipelineLoader.Load(PipelineLoader.EntryFile)["parameters"])
            .Cast<Dictionary<object, object>>()
            .ToList();

        parameters.ShouldAllBe(parameter => parameter.ContainsKey("type") && parameter.ContainsKey("default"));

        string[] expected =
            ["profile", "pushImages", "containerRegistry", "registryServiceConnection", "releaseEnvironment", "variableGroup", "vmImage"];

        parameters.Select(parameter => (string)parameter["name"]).ShouldBe(expected);

        Default(parameters, "pushImages").ShouldBe("false");
        Default(parameters, "profile").ShouldBe("auto");
        Default(parameters, "containerRegistry").ShouldBeEmpty();
        Default(parameters, "registryServiceConnection").ShouldBeEmpty();
        Default(parameters, "variableGroup").ShouldBeEmpty();
    }

    [Fact]
    public void TheVariableGroupIsLinkedOnlyWhenTheParameterNamesIt()
    {
        PipelineLoader.RootVariableGroups().ShouldBe(["${{ parameters.variableGroup }}"]);
        RepositoryFiles.ReadAllText(PipelineLoader.EntryFile).ShouldContain("${{ if ne(parameters.variableGroup, '') }}");
    }

    [Fact]
    public void EveryTemplateCallSuppliesOnlyDeclaredParametersAndAllTheOnesWithoutDefault()
    {
        foreach (var call in PipelineLoader.TemplateCalls())
        {
            var declared = PipelineLoader.DeclaredParameters(call.To);

            call.SuppliedParameters.ShouldAllBe(
                name => declared.Any(parameter => parameter.Name == name),
                $"{call.From} passes a parameter that {call.To} does not declare.");

            declared.Where(parameter => !parameter.HasDefault).Select(parameter => parameter.Name)
                .ShouldAllBe(
                    name => call.SuppliedParameters.Contains(name),
                    $"{call.From} does not pass a required parameter of {call.To}.");
        }
    }

    [Fact]
    public void TheTestStageTemplateOnlyAcceptsTheQuickAndTheFullProfilesAndTheQuickOneIsChosenForPullRequests()
    {
        var profile = PipelineLoader.DeclaredParameters("pipelines/stages/test.yml").Single(parameter => parameter.Name == "profile");

        profile.Type.ShouldBe("string");
        profile.Default.ShouldBe("full");
        RepositoryFiles.ReadAllText("pipelines/stages/test.yml").ShouldContain("- quick");
        RepositoryFiles.ReadAllText(PipelineLoader.EntryFile).ShouldContain("eq(variables['Build.Reason'], 'PullRequest')");
    }

    [Fact]
    public void TriggersCoverMainAndPullRequests()
    {
        var entry = PipelineLoader.Load(PipelineLoader.EntryFile);

        entry.ShouldContainKey("trigger");
        entry.ShouldContainKey("pr");
    }

    [Fact]
    public void NoPipelineFileDependsOnAFeatureOfOnlyOneRepositoryHost()
    {
        foreach (var file in PipelineLoader.TemplateFiles().Append(PipelineLoader.EntryFile))
        {
            var text = RepositoryFiles.ReadAllText(file);

            foreach (var token in HostSpecificTokens)
            {
                text.ShouldNotContain(token, Case.Insensitive, $"{file} uses {token}.");
            }
        }
    }

    [Fact]
    public void ArtifactNamesAreUniqueSoThatNoRunPublishesTwoArtifactsWithTheSameName()
    {
        var names = PipelineLoader.AllJobs().SelectMany(ArtifactsOf).ToList();

        names.ShouldNotBeEmpty();
        names.ShouldBeUnique();
    }

    [Fact]
    public void PipelineFilesCarryNoSecretVariablesAndNoFixedVariableGroup()
    {
        var files = PipelineLoader.TemplateFiles().Append(PipelineLoader.EntryFile);

        foreach (var file in files)
        {
            var text = RepositoryFiles.ReadAllText(file);

            text.ShouldNotContain("isSecret");
            text.ShouldNotContain("-----BEGIN");
            text.Split('\n')
                .Where(line => line.TrimStart().StartsWith("- group:", StringComparison.Ordinal))
                .ShouldAllBe(line => line.Contains("${{ parameters.", StringComparison.Ordinal));
        }
    }

    private static List<string> ArtifactsOf(PipelineJob job)
    {
        return job.Steps
            .Where(step => step.Task == "PublishPipelineArtifact@1")
            .Select(step => step.Inputs["artifact"])
            .ToList();
    }

    private static string Default(List<Dictionary<object, object>> parameters, string name)
    {
        return Convert.ToString(parameters.Single(parameter => (string)parameter["name"] == name)["default"], CultureInfo.InvariantCulture)
               ?? string.Empty;
    }

    private static bool Reaches(IReadOnlyDictionary<string, IReadOnlyList<string>> graph, string from, string target,
        HashSet<string> visited)
    {
        foreach (var dependency in graph[from])
        {
            if (dependency == target)
            {
                return true;
            }

            if (visited.Add(dependency) && graph.ContainsKey(dependency) && Reaches(graph, dependency, target, visited))
            {
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(@"^\s*set -[A-Za-z]*e", RegexOptions.Multiline)]
    private static partial Regex StrictShell();

    [GeneratedRegex(@"^[A-Za-z0-9]+@\d+$")]
    private static partial Regex PinnedTask();

    [GeneratedRegex(@"^FROM \S+@sha256:[0-9a-f]{64}( AS \w+)?\s*$")]
    private static partial Regex FromWithDigest();
}
