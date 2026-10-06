extern alias LedgerWorker;

using System.Reflection;
using System.Text.RegularExpressions;
using Ledger.Architecture.Tests.Layers;

namespace Ledger.Architecture.Tests.Hygiene;

[Trait("Category", "Architecture")]
public sealed class ConfigurationIsReadTests
{
    private const string OptionsSuffix = "Options";

    private static readonly Lazy<IReadOnlyList<(string Path, string Text)>> Sources = new(LoadSources);

    public static TheoryData<string, string> OptionProperties()
    {
        var data = new TheoryData<string, string>();

        foreach (var type in OptionTypes())
        {
            foreach (var property in PublicProperties(type))
            {
                data.Add(type.Name, property.Name);
            }
        }

        return data;
    }

    [Fact]
    public void TheOptionTypes_IncludeTheResilienceSection()
    {
        OptionTypes().Select(type => type.Name).ShouldContain("ResilienceOptions");
        OptionTypes().Select(type => type.Name).ShouldContain("ResilienceHealthOptions");
        OptionTypes().Select(type => type.Name).ShouldContain("ResilienceRetryOptions");
    }

    [Theory]
    [MemberData(nameof(OptionProperties))]
    public void EveryConfigurableProperty_IsReadByTheCodeThatItConfigures(string typeName, string propertyName)
    {
        var type = OptionTypes().Single(candidate => candidate.Name == typeName);
        var escaped = Regex.Escape(propertyName);
        var access = new Regex($@"\.{escaped}\b", RegexOptions.CultureInvariant);
        var mention = new Regex($@"\b{escaped}\b", RegexOptions.CultureInvariant);
        var declaration = new Regex($@"\b(public|internal)\b[^;]*\b{escaped}\s*\{{", RegexOptions.CultureInvariant);

        var readers = Sources.Value
            .Where(source => !IsValidator(source.Path))
            .Where(source => IsDefinition(source.Path, type)
                ? ReadsItself(source.Text, mention, declaration)
                : access.IsMatch(source.Text))
            .Select(source => source.Path)
            .ToList();

        readers.ShouldNotBeEmpty($"{typeName}.{propertyName} is bound from configuration but nothing in src reads it");
    }

    [Theory]
    [InlineData("    [Range(1, 60)] public int Seconds { get; init; } = 3;", false)]
    [InlineData("    public Settings ToSettings() => new(TimeSpan.FromSeconds(Seconds));", true)]
    [InlineData("    public int Other { get; init; } = DefaultSeconds;", false)]
    public void TheSelfReadDetector_IgnoresTheDeclarationAndCountsAnyOtherMention(string line, bool reads)
    {
        var mention = new Regex(@"\bSeconds\b", RegexOptions.CultureInvariant);
        var declaration = new Regex(@"\b(public|internal)\b[^;]*\bSeconds\s*\{", RegexOptions.CultureInvariant);

        ReadsItself(line, mention, declaration).ShouldBe(reads);
    }

    private static bool ReadsItself(string text, Regex mention, Regex declaration)
    {
        return text
            .Split('\n')
            .Any(line => mention.IsMatch(line) && !declaration.IsMatch(line));
    }

    private static bool IsDefinition(string path, Type type) => Path.GetFileName(path) == type.Name + ".cs";

    private static bool IsValidator(string path)
    {
        return Path.GetFileName(path).EndsWith(OptionsSuffix + "Validator.cs", StringComparison.Ordinal);
    }

    private static List<Type> OptionTypes()
    {
        return new[]
            {
                LayerAssemblies.Application, LayerAssemblies.Infrastructure, LayerAssemblies.Api,
                LayerAssemblies.Worker
            }
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false, IsNested: false }
                           && type.Name.EndsWith(OptionsSuffix, StringComparison.Ordinal)
                           && type.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is null
                           && PublicProperties(type).Any())
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<PropertyInfo> PublicProperties(Type type)
    {
        return type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(property => property.SetMethod is { IsPublic: true });
    }

    private static List<(string Path, string Text)> LoadSources()
    {
        return SourceFiles.Under("src")
            .Select(path => (path, File.ReadAllText(path)))
            .ToList();
    }
}
