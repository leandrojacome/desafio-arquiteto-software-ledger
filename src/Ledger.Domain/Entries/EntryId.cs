using System.Text.Json.Serialization;
using Ledger.Domain.Shared;

namespace Ledger.Domain.Entries;

[JsonConverter(typeof(EntryIdJsonConverter))]
public readonly record struct EntryId
{
    private readonly Guid _value;

    private EntryId(Guid value)
    {
        _value = value;
    }

    public Guid Value => _value == Guid.Empty
        ? throw new InvalidOperationException("The entry id was not created by EntryId.From.")
        : _value;

    public static Result<EntryId> From(Guid value) =>
        value == Guid.Empty ? EntryErrors.NotFound : new EntryId(value);

    public static Result<EntryId> From(string? text) =>
        HyphenatedGuid.TryParse(text, out var value) ? From(value) : EntryErrors.NotFound;

    public override string ToString() => HyphenatedGuid.ToText(_value);
}
