using System.Globalization;
using YamlDotNet.Serialization;

namespace Ledger.Architecture.Tests.Ci;

internal static class PipelineLoader
{
    public const string EntryFile = "azure-pipelines.yml";

    private const string TemplateDirectory = "pipelines";

    private static readonly string[] ConditionalKeyPrefixes = ["${{ if", "${{ elseif", "${{ else"];

    private static readonly string[] ScriptKeys = ["pwsh", "bash", "script", "powershell"];

    private static readonly IDeserializer Deserializer = new DeserializerBuilder().Build();

    public static Dictionary<object, object> Load(string relativePath)
    {
        return Deserializer.Deserialize<Dictionary<object, object>>(RepositoryFiles.ReadAllText(relativePath));
    }

    public static IReadOnlyList<string> TemplateFiles()
    {
        var root = RepositoryFiles.PathOf(TemplateDirectory);

        return Directory
            .GetFiles(root, "*.yml", SearchOption.AllDirectories)
            .Select(path => $"{TemplateDirectory}/{Path.GetRelativePath(root, path).Replace('\\', '/')}")
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    public static IReadOnlyList<PipelineStage> Stages()
    {
        var entry = Load(EntryFile);

        return StagesOf(EntryFile, AsList(entry["stages"])).ToList();
    }

    public static IReadOnlyList<PipelineJob> AllJobs()
    {
        return Stages().SelectMany(stage => stage.Jobs).ToList();
    }

    public static PipelineStage Stage(string name)
    {
        return Stages().Single(stage => stage.Name == name);
    }

    public static PipelineJob Job(string name)
    {
        return AllJobs().Single(job => job.Name == name);
    }

    public static IEnumerable<string> Scripts(PipelineJob job)
    {
        return job.Steps.Select(step => step.Script).OfType<string>();
    }

    public static IReadOnlyList<string> ReferencedTemplates()
    {
        return TemplateCalls().Select(call => call.To).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
    }

    public static IReadOnlyList<TemplateCall> TemplateCalls()
    {
        var found = new List<TemplateCall>();

        CollectTemplates(EntryFile, Load(EntryFile), found);

        return found;
    }

    public static IReadOnlyList<TemplateParameter> DeclaredParameters(string file)
    {
        var data = Load(file);

        if (!data.TryGetValue("parameters", out var declared))
        {
            return [];
        }

        return AsList(declared)
            .Select(AsMap)
            .Select(map => new TemplateParameter(
                Text(map, "name") ?? string.Empty,
                Text(map, "type") ?? "string",
                map.ContainsKey("default"),
                Text(map, "default")))
            .ToList();
    }

    public static IReadOnlyDictionary<string, string> RootVariables()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);

        CollectVariables(EntryFile, AsList(Load(EntryFile)["variables"]), variables);

        return variables;
    }

    public static IReadOnlyList<string> RootVariableGroups()
    {
        var groups = new List<string>();

        CollectGroups(AsList(Load(EntryFile)["variables"]), groups);

        return groups;
    }

    private static void CollectVariables(string file, IEnumerable<object> nodes, Dictionary<string, string> variables)
    {
        foreach (var node in Flatten(nodes))
        {
            var map = AsMap(node);

            if (map.TryGetValue("template", out var template))
            {
                var resolved = Resolve(file, (string)template);

                CollectVariables(resolved, AsList(Load(resolved)["variables"]), variables);
            }
            else if (map.ContainsKey("name"))
            {
                variables[Text(map, "name") ?? string.Empty] = Text(map, "value") ?? string.Empty;
            }
        }
    }

    private static void CollectGroups(IEnumerable<object> nodes, List<string> groups)
    {
        foreach (var node in Flatten(nodes))
        {
            var group = Text(AsMap(node), "group");

            if (group is not null)
            {
                groups.Add(group);
            }
        }
    }

    private static void CollectTemplates(string file, object? node, List<TemplateCall> found)
    {
        switch (node)
        {
            case Dictionary<object, object> map:
                foreach (var pair in map)
                {
                    if (pair.Key is "template" && pair.Value is string template)
                    {
                        var resolved = Resolve(file, template);
                        var supplied = map.TryGetValue("parameters", out var parameters)
                            ? SuppliedNames(parameters)
                            : [];

                        found.Add(new TemplateCall(file, resolved, supplied));
                        CollectTemplates(resolved, Load(resolved), found);
                    }
                    else
                    {
                        CollectTemplates(file, pair.Value, found);
                    }
                }

                break;
            case List<object> list:
                foreach (var item in list)
                {
                    CollectTemplates(file, item, found);
                }

                break;
        }
    }

    private static List<string> SuppliedNames(object? parameters)
    {
        var names = new List<string>();

        if (parameters is not Dictionary<object, object> map)
        {
            return names;
        }

        foreach (var pair in map)
        {
            if (pair.Key is string key && IsConditional(key))
            {
                names.AddRange(SuppliedNames(pair.Value));
            }
            else if (pair.Key is string name)
            {
                names.Add(name);
            }
        }

        return names.Distinct(StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<PipelineStage> StagesOf(string file, IEnumerable<object> nodes)
    {
        foreach (var node in Flatten(nodes))
        {
            var map = AsMap(node);

            if (map.TryGetValue("template", out var template))
            {
                var resolved = Resolve(file, (string)template);

                foreach (var stage in StagesOf(resolved, AsList(Load(resolved)["stages"])))
                {
                    yield return stage;
                }

                continue;
            }

            yield return new PipelineStage(
                Text(map, "stage") ?? string.Empty,
                DependsOnOf(map),
                Text(map, "condition"),
                JobsOf(file, AsList(map["jobs"])).ToList());
        }
    }

    private static IEnumerable<PipelineJob> JobsOf(string file, IEnumerable<object> nodes)
    {
        foreach (var node in Flatten(nodes))
        {
            var map = AsMap(node);

            if (map.TryGetValue("template", out var template))
            {
                var resolved = Resolve(file, (string)template);

                foreach (var job in JobsOf(resolved, AsList(Load(resolved)["jobs"])))
                {
                    yield return job;
                }

                continue;
            }

            yield return ToJob(file, map);
        }
    }

    private static List<string> DependsOnOf(Dictionary<object, object> map)
    {
        if (!map.TryGetValue("dependsOn", out var dependencies))
        {
            return [];
        }

        return dependencies is string single
            ? [single]
            : Flatten(AsList(dependencies)).Select(item => (string)item).ToList();
    }

    private static PipelineJob ToJob(string file, Dictionary<object, object> map)
    {
        var isDeployment = map.ContainsKey("deployment");

        var variables = map.TryGetValue("variables", out var declared)
            ? AsMap(declared).ToDictionary(pair => (string)pair.Key,
                pair => Convert.ToString(pair.Value, CultureInfo.InvariantCulture) ?? string.Empty)
            : [];

        var timeout = int.TryParse(Text(map, "timeoutInMinutes"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
            ? minutes
            : (int?)null;

        var steps = isDeployment
            ? AsList(AsMap(AsMap(AsMap(map["strategy"])["runOnce"])["deploy"])["steps"])
            : AsList(map["steps"]);

        return new PipelineJob(
            Text(map, isDeployment ? "deployment" : "job") ?? string.Empty,
            DependsOnOf(map),
            variables,
            StepsOf(file, steps).ToList(),
            timeout,
            isDeployment,
            Text(map, "environment"));
    }

    private static IEnumerable<PipelineStep> StepsOf(string file, IEnumerable<object> nodes)
    {
        foreach (var node in Flatten(nodes))
        {
            var map = AsMap(node);

            if (map.TryGetValue("template", out var template))
            {
                var resolved = Resolve(file, (string)template);

                foreach (var step in StepsOf(resolved, AsList(Load(resolved)["steps"])))
                {
                    yield return step;
                }

                continue;
            }

            yield return ToStep(map);
        }
    }

    private static PipelineStep ToStep(Dictionary<object, object> map)
    {
        var scriptKey = ScriptKeys.FirstOrDefault(key => map.ContainsKey(key));

        return new PipelineStep(
            scriptKey is null ? null : Text(map, scriptKey),
            Text(map, "task"),
            Text(map, "condition"),
            map.ContainsKey("checkout"),
            Text(map, "checkout"),
            Text(map, "fetchDepth"),
            Text(map, "displayName"),
            Strings(map, "inputs"),
            Strings(map, "env"),
            scriptKey);
    }

    private static Dictionary<string, string> Strings(Dictionary<object, object> map, string key)
    {
        return map.TryGetValue(key, out var value)
            ? AsMap(value).ToDictionary(pair => (string)pair.Key,
                pair => Convert.ToString(pair.Value, CultureInfo.InvariantCulture) ?? string.Empty)
            : [];
    }

    private static IEnumerable<object> Flatten(IEnumerable<object> nodes)
    {
        foreach (var node in nodes)
        {
            if (node is Dictionary<object, object> map && map.Count == 1 && map.Keys.First() is string key && IsConditional(key))
            {
                foreach (var inner in Flatten(AsList(map.Values.First())))
                {
                    yield return inner;
                }
            }
            else
            {
                yield return node;
            }
        }
    }

    private static bool IsConditional(string key)
    {
        return ConditionalKeyPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static string Resolve(string includingFile, string template)
    {
        if (template.StartsWith('/'))
        {
            return template[1..];
        }

        var directory = Path.GetDirectoryName(includingFile)?.Replace('\\', '/') ?? string.Empty;

        return directory.Length == 0 ? template : $"{directory}/{template}";
    }

    private static List<object> AsList(object? node)
    {
        return node as List<object> ?? [];
    }

    private static Dictionary<object, object> AsMap(object? node)
    {
        return node as Dictionary<object, object> ?? [];
    }

    private static string? Text(Dictionary<object, object> map, string key)
    {
        return map.TryGetValue(key, out var value) ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;
    }
}
