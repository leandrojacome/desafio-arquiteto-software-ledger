using System.Reflection;
using System.Text.Json;
using Ledger.Architecture.Tests.Ci;
using Ledger.Architecture.Tests.Layers;

namespace Ledger.Architecture.Tests.Hygiene;

[Trait("Category", "Architecture")]
public sealed class AppSettingsKeysAreBoundTests
{
    private const string SectionNameField = "SectionName";
    private const char Separator = ':';

    private static readonly string[] FrameworkSections = ["Serilog", "AllowedHosts"];

    private static readonly Lazy<IReadOnlyList<(string Section, Type Type)>> Sections = new(LoadSections);

    public static TheoryData<string> Files() => new()
    {
        "src/Ledger.Api/appsettings.json",
        "src/Ledger.Api/appsettings.Development.json",
        "src/Ledger.Worker/appsettings.json",
        "src/Ledger.Worker/appsettings.Development.json"
    };

    [Theory]
    [MemberData(nameof(Files))]
    public void EveryKeyOfTheFile_IsBoundByAnOptionsProperty(string file)
    {
        using var document = JsonDocument.Parse(RepositoryFiles.ReadAllText(file));

        var unbound = KeysOf(document.RootElement, string.Empty)
            .Where(key => !IsBound(key))
            .ToList();

        unbound.ShouldBeEmpty($"{file} has keys that no options class reads, so editing them changes nothing");
    }

    [Theory]
    [InlineData("Worker:FailureBackoff:MinSeconds", true)]
    [InlineData("Postgres:Sources:Worker:Username", true)]
    [InlineData("Resilience:Health:OutboxLagSeconds", true)]
    [InlineData("Security:ForwardedHeaders:KnownNetworks", true)]
    [InlineData("Serilog:MinimumLevel:Default", true)]
    [InlineData("AllowedHosts", true)]
    [InlineData("Outbox:FailureBackoff:MinSeconds", false)]
    [InlineData("Outbox:BatchSizes", false)]
    [InlineData("Postgres:Sources:Reporter:Username", false)]
    [InlineData("Unknown:Key", false)]
    public void TheCheck_RecognisesBoundAndUnboundKeys(string key, bool bound)
    {
        IsBound(key).ShouldBe(bound);
    }

    private static IEnumerable<string> KeysOf(JsonElement element, string prefix)
    {
        foreach (var property in element.EnumerateObject())
        {
            var path = prefix.Length == 0 ? property.Name : prefix + Separator + property.Name;

            if (property.Value.ValueKind == JsonValueKind.Object && property.Value.EnumerateObject().Any())
            {
                foreach (var nested in KeysOf(property.Value, path))
                {
                    yield return nested;
                }
            }
            else
            {
                yield return path;
            }
        }
    }

    private static bool IsBound(string key)
    {
        var segments = key.Split(Separator);

        if (FrameworkSections.Contains(segments[0], StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return Sections.Value.Any(section => ResolvesThrough(section, key, segments));
    }

    private static bool ResolvesThrough((string Section, Type Type) section, string key, string[] segments)
    {
        var prefix = section.Section + Separator;

        if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var skipped = section.Section.Split(Separator).Length;

        return Resolves(section.Type, segments.Skip(skipped).ToArray());
    }

    private static bool Resolves(Type type, string[] segments)
    {
        var current = type;

        for (var index = 0; index < segments.Length; index++)
        {
            var property = current
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(candidate => string.Equals(candidate.Name, segments[index], StringComparison.OrdinalIgnoreCase));

            if (property is null)
            {
                return false;
            }

            current = property.PropertyType;

            if (index < segments.Length - 1 && ValueTypeOfDictionary(current) is { } valueType)
            {
                index++;
                current = valueType;
            }
        }

        return true;
    }

    private static Type? ValueTypeOfDictionary(Type type)
    {
        return type
            .GetInterfaces()
            .Append(type)
            .Where(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>))
            .Select(candidate => candidate.GetGenericArguments()[1])
            .FirstOrDefault();
    }

    private static List<(string Section, Type Type)> LoadSections()
    {
        return new[]
            {
                LayerAssemblies.Application, LayerAssemblies.Infrastructure, LayerAssemblies.Api,
                LayerAssemblies.Worker
            }
            .SelectMany(assembly => assembly.GetTypes())
            .Select(type => (Section: SectionOf(type), Type: type))
            .Where(candidate => candidate.Section is not null)
            .Select(candidate => (Section: candidate.Section ?? string.Empty, candidate.Type))
            .ToList();
    }

    private static string? SectionOf(Type type)
    {
        var field = type.GetField(SectionNameField, BindingFlags.Public | BindingFlags.Static);

        return field is { IsLiteral: true } && field.FieldType == typeof(string)
            ? (string?)field.GetRawConstantValue()
            : null;
    }
}
