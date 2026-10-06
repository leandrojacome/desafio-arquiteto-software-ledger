using System.Globalization;
using System.Text.RegularExpressions;

namespace Ledger.Architecture.Tests.Ci;

[Trait("Category", "Architecture")]
public sealed partial class GitHubWorkflowTests
{
    private const string WorkflowFile = ".github/workflows/ci.yml";

    [Fact]
    public void TheWorkflowExistsAndHoldsTheExpectedJobs()
    {
        RepositoryFiles.Exists(WorkflowFile).ShouldBeTrue();

        Jobs().Keys.ShouldBe(["validate", "unit", "integration"], ignoreOrder: true);
    }

    [Fact]
    public void ItRunsOnPushesToMainPullRequestsToMainAndOnDemand()
    {
        var triggers = Workflow()["on"].ShouldBeOfType<Dictionary<object, object>>();

        triggers.Keys.Cast<string>().ShouldBe(["push", "pull_request", "workflow_dispatch"], ignoreOrder: true);
        BranchesOf(triggers["push"]).ShouldBe(["main"]);
        BranchesOf(triggers["pull_request"]).ShouldBe(["main"]);
    }

    [Fact]
    public void TheTokenIsReadOnlyAndNoJobAsksForMore()
    {
        var permissions = Workflow()["permissions"].ShouldBeOfType<Dictionary<object, object>>();

        permissions.Select(pair => $"{pair.Key}={pair.Value}").ShouldBe(["contents=read"]);
        Jobs().Values.ShouldAllBe(job => !job.ContainsKey("permissions"));
    }

    [Fact]
    public void TheWorkflowNeverRunsCodeOfAPullRequestWithTheTokenOfTheRepositoryOrReadsSecrets()
    {
        var text = RepositoryFiles.ReadAllText(WorkflowFile);

        text.ShouldNotContain("pull_request_target");
        text.ShouldNotContain("secrets.");
    }

    [Theory]
    [InlineData("validate", "")]
    [InlineData("unit", "validate")]
    [InlineData("integration", "validate")]
    public void EachJobWaitsForTheExpectedJobs(string name, string expected)
    {
        var needs = Jobs()[name].TryGetValue("needs", out var value) ? Convert.ToString(value, CultureInfo.InvariantCulture) : string.Empty;

        needs.ShouldBe(expected);
    }

    [Theory]
    [InlineData("validate", 25)]
    [InlineData("unit", 20)]
    [InlineData("integration", 60)]
    public void EveryJobKeepsAtLeastTheTimeoutItsExpectedDurationNeeds(string name, int minimumMinutes)
    {
        var job = Jobs()[name];

        job.ShouldContainKey("timeout-minutes");
        Convert.ToInt32(job["timeout-minutes"], CultureInfo.InvariantCulture).ShouldBeGreaterThanOrEqualTo(minimumMinutes);
    }

    [Fact]
    public void EveryJobStartsWithAShallowCheckout()
    {
        foreach (var (name, job) in Jobs())
        {
            var first = StepsOf(job)[0];

            first["uses"].ShouldBeOfType<string>().ShouldStartWith("actions/checkout@", Case.Sensitive, $"{name} does not start with a checkout.");
            first["with"].ShouldBeOfType<Dictionary<object, object>>()["fetch-depth"].ShouldBe("1");
        }
    }

    [Fact]
    public void ActionsArePinnedByMajorVersion()
    {
        var actions = AllSteps().Select(step => step.GetValueOrDefault("uses")).OfType<string>().Distinct(StringComparer.Ordinal).ToList();

        actions.ShouldNotBeEmpty();
        actions.ShouldAllBe(action => PinnedAction().IsMatch(action));
    }

    [Fact]
    public void EveryJobThatNeedsDockerInheritsTheVariableThatMakesItFailInsteadOfSkipping()
    {
        var environment = Workflow()["env"].ShouldBeOfType<Dictionary<object, object>>();

        environment["LEDGER_REQUIRE_DOCKER"].ShouldBe("true");
        AllSteps().ShouldAllBe(step => !step.ContainsKey("env") || !((Dictionary<object, object>)step["env"]).ContainsKey("LEDGER_REQUIRE_DOCKER"));
    }

    [Fact]
    public void EveryCommandTheWorkflowRunsIsOneThatTheAzurePipelineAlsoRuns()
    {
        var azure = PipelineLoader.AllJobs()
            .SelectMany(job => PipelineLoader.Scripts(job))
            .SelectMany(CommandLines)
            .ToHashSet(StringComparer.Ordinal);

        var workflow = AllSteps()
            .Select(step => step.GetValueOrDefault("run"))
            .OfType<string>()
            .SelectMany(CommandLines)
            .ToList();

        workflow.ShouldNotBeEmpty();

        foreach (var command in workflow)
        {
            azure.ShouldContain(command, $"'{command}' runs in the workflow and not in the Azure pipeline.");
        }
    }

    [Fact]
    public void TheWorkflowRunsPlainCommandsInBash()
    {
        var text = RepositoryFiles.ReadAllText(WorkflowFile);

        text.ShouldNotContain("pwsh");
        text.ShouldNotContain(".ps1");
        Workflow().ShouldNotContainKey("defaults");
    }

    [Fact]
    public void IntegrationUsesTheQuickProfileOnlyInPullRequests()
    {
        var steps = StepsOf(Jobs()["integration"]);

        var quick = steps.Single(step => RunOf(step).Contains("--filter \"Speed!=Slow\"", StringComparison.Ordinal));
        var full = steps.Single(step =>
            RunOf(step).Contains("dotnet test tests/Ledger.Api.IntegrationTests", StringComparison.Ordinal)
            && !RunOf(step).Contains("--filter", StringComparison.Ordinal));

        quick["if"].ShouldBe("${{ github.event_name == 'pull_request' }}");
        full["if"].ShouldBe("${{ github.event_name != 'pull_request' }}");
    }

    [Fact]
    public void TheResultsAreUploadedEvenWhenTheTestsFail()
    {
        var uploads = AllSteps().Where(step => ((string?)step.GetValueOrDefault("uses"))?.StartsWith("actions/upload-artifact@", StringComparison.Ordinal) == true).ToList();

        uploads.Count.ShouldBe(2);
        uploads.ShouldAllBe(step => (string)step["if"] == "${{ !cancelled() }}");
        uploads.Select(step => ((Dictionary<object, object>)step["with"])["name"]).ShouldBeUnique();
    }

    [Fact]
    public void NoStepReachesARegistry()
    {
        var text = RepositoryFiles.ReadAllText(WorkflowFile);

        text.ShouldNotContain("docker push");
        text.ShouldNotContain("docker login");
    }

    private static Dictionary<object, object> Workflow()
    {
        return PipelineLoader.Load(WorkflowFile);
    }

    private static Dictionary<string, Dictionary<object, object>> Jobs()
    {
        return Workflow()["jobs"].ShouldBeOfType<Dictionary<object, object>>()
            .ToDictionary(pair => (string)pair.Key, pair => (Dictionary<object, object>)pair.Value, StringComparer.Ordinal);
    }

    private static List<Dictionary<object, object>> StepsOf(Dictionary<object, object> job)
    {
        return ((List<object>)job["steps"]).Cast<Dictionary<object, object>>().ToList();
    }

    private static List<Dictionary<object, object>> AllSteps()
    {
        return Jobs().Values.SelectMany(StepsOf).ToList();
    }

    private static List<string> BranchesOf(object trigger)
    {
        return ((List<object>)((Dictionary<object, object>)trigger)["branches"]).Cast<string>().ToList();
    }

    private static string RunOf(Dictionary<object, object> step)
    {
        return (string?)step.GetValueOrDefault("run") ?? string.Empty;
    }

    private static IEnumerable<string> CommandLines(string script)
    {
        return script.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0);
    }

    [GeneratedRegex(@"^[A-Za-z0-9._-]+/[A-Za-z0-9._-]+@v\d+$")]
    private static partial Regex PinnedAction();
}
