using System.Globalization;
using Ledger.Application.Security;
using Ledger.Infrastructure.Observability;
using Ledger.Infrastructure.Tests.Observability.Support;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Ledger.Infrastructure.Tests.Observability;

[Trait("Category", "Unit")]
public sealed class SensitiveDataDestructuringPolicyTests
{
    private const string Mask = "***";

    private readonly CollectingLogSink _sink = new();

    private Logger Logger() =>
        new LoggerConfiguration()
            .Destructure.With<SensitiveDataDestructuringPolicy>()
            .WriteTo.Sink(_sink)
            .CreateLogger();

    private StructureValue Destructure(object payload)
    {
        using var logger = Logger();
        logger.Information("{@Payload}", payload);

        return _sink.Events.Single().Properties["Payload"].ShouldBeOfType<StructureValue>();
    }

    private static string ValueOf(StructureValue structure, string name)
    {
        using var writer = new StringWriter();
        structure.Properties.Single(property => property.Name == name).Value.Render(writer, null, CultureInfo.InvariantCulture);

        return writer.ToString();
    }

    [Fact]
    public void Destructure_PropertyMarkedAsSensitive_IsMasked()
    {
        var structure = Destructure(new Marked { Name = "visible", Reference = "E18236120202610011403s0a1b2c3d4e" });

        ValueOf(structure, "Reference").ShouldBe($"\"{Mask}\"");
        ValueOf(structure, "Name").ShouldBe("\"visible\"");
    }

    [Fact]
    public void Destructure_EveryForbiddenName_IsMaskedWhateverTheCase()
    {
        var structure = Destructure(
            new Forbidden
            {
                Password = "p1",
                Secret = "s1",
                Token = "t1",
                Authorization = "Bearer abc",
                ConnectionString = "Host=db;Password=p2",
                ApiKey = "k1",
                HolderDocument = "12345678909",
                AccessToken = "t2",
                ClientSecret = "s2",
                Document = "98765432100",
                Cpf = "12345678909",
                Cnpj = "12345678000195",
                IdempotencyKey = "qs-debit-0001",
                Name = "visible",
                Currency = "BRL",
                Amount = 80.00m
            });

        foreach (var name in new[]
                 {
                     "Password", "Secret", "Token", "Authorization", "ConnectionString", "ApiKey", "HolderDocument",
                     "AccessToken", "ClientSecret", "Document", "Cpf", "Cnpj", "IdempotencyKey"
                 })
        {
            ValueOf(structure, name).ShouldBe($"\"{Mask}\"", name);
        }

        ValueOf(structure, "Name").ShouldBe("\"visible\"");
        ValueOf(structure, "Currency").ShouldBe("\"BRL\"");
        ValueOf(structure, "Amount").ShouldBe("80.00");
    }

    [Fact]
    public void Destructure_RecordWithTheAttributeOnThePropertyTarget_IsMasked()
    {
        var structure = Destructure(new PropertyTargeted("account-1", [1, 2, 3]));

        ValueOf(structure, "Id").ShouldBe("\"account-1\"");
        ValueOf(structure, "Encrypted").ShouldBe($"\"{Mask}\"");
    }

    [Fact]
    public void Destructure_RecordWithTheAttributeOnTheConstructorParameter_IsMasked()
    {
        var structure = Destructure(new ParameterTargeted("account-1", "value that must not leak"));

        ValueOf(structure, "Id").ShouldBe("\"account-1\"");
        ValueOf(structure, "Hidden").ShouldBe($"\"{Mask}\"");
    }

    [Fact]
    public void Destructure_NestedObject_IsMaskedInsideTheParent()
    {
        var structure = Destructure(
            new Outer { Label = "outer", Child = new Marked { Name = "inner", Reference = "must not leak" } });

        var child = structure.Properties.Single(property => property.Name == "Child").Value
            .ShouldBeOfType<StructureValue>();
        ValueOf(structure, "Label").ShouldBe("\"outer\"");
        ValueOf(child, "Name").ShouldBe("\"inner\"");
        ValueOf(child, "Reference").ShouldBe($"\"{Mask}\"");
    }

    [Fact]
    public void Destructure_CollectionOfObjects_MasksEveryElement()
    {
        using var logger = Logger();
        List<Marked> items =
        [
            new() { Name = "first", Reference = "secret-1" },
            new() { Name = "second", Reference = "secret-2" }
        ];

        logger.Information("{@Items}", items);

        var json = _sink.AllJson();
        json.ShouldNotContain("secret-1");
        json.ShouldNotContain("secret-2");
        json.ShouldContain("first");
        json.ShouldContain("second");
        var sequence = _sink.Events.Single().Properties["Items"].ShouldBeOfType<SequenceValue>();
        sequence.Elements.Count.ShouldBe(2);
    }

    [Fact]
    public void Destructure_DictionaryOfObjects_MasksEveryValue()
    {
        using var logger = Logger();
        var items = new Dictionary<string, Marked> { ["a"] = new() { Name = "n", Reference = "secret-3" } };

        logger.Information("{@Items}", items);

        _sink.AllJson().ShouldNotContain("secret-3");
    }

    [Fact]
    public void Destructure_ObjectInsideAnArray_IsMasked()
    {
        using var logger = Logger();

        logger.Information("{@Items}", new[] { new Marked { Name = "n", Reference = "secret-4" } });

        _sink.AllJson().ShouldNotContain("secret-4");
    }

    [Fact]
    public void Destructure_ObjectWithoutSensitiveMembers_IsLeftToTheDefaultBehavior()
    {
        var policy = new SensitiveDataDestructuringPolicy();
        var factory = new FactoryStub();

        var handled = policy.TryDestructure(new Plain { Name = "n", Amount = 1m }, factory, out var result);

        handled.ShouldBeFalse();
        result.ShouldBeNull();
    }

    [Fact]
    public void Destructure_ObjectWithoutSensitiveMembers_StillShowsItsPropertiesInTheLog()
    {
        var structure = Destructure(new Plain { Name = "n", Amount = 12.5m });

        ValueOf(structure, "Name").ShouldBe("\"n\"");
        ValueOf(structure, "Amount").ShouldBe("12.5");
    }

    [Theory]
    [InlineData("a string")]
    [InlineData(42)]
    [InlineData(true)]
    public void TryDestructure_ForScalars_ReturnsFalse(object value)
    {
        new SensitiveDataDestructuringPolicy().TryDestructure(value, new FactoryStub(), out _).ShouldBeFalse();
    }

    [Fact]
    public void Destructure_GetterThatThrows_DoesNotBreakLogging()
    {
        var structure = Destructure(new Throwing { Password = "x" });

        ValueOf(structure, "Boom").ShouldContain("InvalidOperationException");
        ValueOf(structure, "Password").ShouldBe($"\"{Mask}\"");
    }

    [Fact]
    public void Destructure_ObjectGraphWithACycle_TerminatesAndStaysMasked()
    {
        var node = new Node { Name = "loop", Token = "secret-5" };
        node.Next = node;

        using var logger = Logger();
        logger.Information("{@Node}", node);

        _sink.AllJson().ShouldNotContain("secret-5");
    }

    [Fact]
    public void Destructure_DoesNotTouchValuesLoggedWithoutTheDestructuringOperator()
    {
        using var logger = Logger();

        logger.Information("{Payload}", new Plain { Name = "n", Amount = 3m });

        var property = _sink.Events.Single().Properties["Payload"];
        property.ShouldBeOfType<ScalarValue>();
    }

    private sealed class Marked
    {
        public string? Name { get; init; }

        [Sensitive]
        public string? Reference { get; init; }
    }

    private sealed class Forbidden
    {
        public string? Password { get; init; }

        public string? Secret { get; init; }

        public string? Token { get; init; }

        public string? Authorization { get; init; }

        public string? ConnectionString { get; init; }

        public string? ApiKey { get; init; }

        public string? HolderDocument { get; init; }

        public string? AccessToken { get; init; }

        public string? ClientSecret { get; init; }

        public string? Document { get; init; }

        public string? Cpf { get; init; }

        public string? Cnpj { get; init; }

        public string? IdempotencyKey { get; init; }

        public string? Name { get; init; }

        public string? Currency { get; init; }

        public decimal Amount { get; init; }
    }

    private sealed record PropertyTargeted(string Id, [property: Sensitive] int[] Encrypted);

    private sealed record ParameterTargeted(string Id, [Sensitive] string Hidden);

    private sealed class Outer
    {
        public string? Label { get; init; }

        public Marked? Child { get; init; }
    }

    private sealed class Plain
    {
        public string? Name { get; init; }

        public decimal Amount { get; init; }
    }

    private sealed class Throwing
    {
        public string? Password { get; init; }

        public string Boom => Password is null ? string.Empty : throw new InvalidOperationException("boom");
    }

    private sealed class Node
    {
        public string? Name { get; init; }

        public string? Token { get; init; }

        public Node? Next { get; set; }
    }

    private sealed class FactoryStub : Serilog.Core.ILogEventPropertyValueFactory
    {
        public LogEventPropertyValue CreatePropertyValue(object? value, bool destructureObjects = false) =>
            new ScalarValue(value);
    }
}
