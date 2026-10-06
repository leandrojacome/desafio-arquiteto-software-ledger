using System.Text.Json.Serialization;
using Ledger.Domain.Shared;

namespace Ledger.Domain.Accounts;

[JsonConverter(typeof(AccountIdJsonConverter))]
public readonly record struct AccountId
{
    private readonly Guid _value;

    private AccountId(Guid value)
    {
        _value = value;
    }

    public Guid Value => _value == Guid.Empty
        ? throw new InvalidOperationException("The account id was not created by AccountId.From.")
        : _value;

    public static Result<AccountId> From(Guid value) =>
        value == Guid.Empty ? AccountErrors.NotFound : new AccountId(value);

    public static Result<AccountId> From(string? text) =>
        HyphenatedGuid.TryParse(text, out var value) ? From(value) : AccountErrors.NotFound;

    public override string ToString() => HyphenatedGuid.ToText(_value);
}
