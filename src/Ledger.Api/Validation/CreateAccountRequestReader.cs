using System.Collections.Frozen;
using Ledger.Domain.Accounts;
using Ledger.Domain.Shared;

namespace Ledger.Api.Validation;

internal static class CreateAccountRequestReader
{
    private const string HolderDocumentField = "holderDocument";
    private const string CurrencyField = "currency";
    private const string OverdraftLimitField = "overdraftLimit";
    private const string SupportedCurrency = "BRL";

    private static readonly FrozenSet<string> KnownFields = new[]
    {
        HolderDocumentField, CurrencyField, OverdraftLimitField
    }.ToFrozenSet(StringComparer.Ordinal);

    public static ReadResult<CreateAccountInput> Read(ReadOnlySpan<byte> body)
    {
        using var parsed = JsonObjectBody.TryParse(body, out var rootIssue);

        if (parsed is null)
        {
            return ReadResult.Invalid<CreateAccountInput>([rootIssue ?? FieldIssues.InvalidJson()]);
        }

        var issues = new List<ValidationIssue>();

        var document = ReadDocument(parsed, issues);
        var currency = ReadCurrency(parsed, issues);
        var overdraft = BodyFields.Amount(parsed, OverdraftLimitField, false, true, issues);

        issues.AddRange(parsed.UnknownFields(KnownFields));

        var limit = BuildLimit(overdraft, currency, issues);

        if (issues.Count == 0 && document is { } holderDocument && currency is not null && limit is not null)
        {
            return ReadResult.Valid(new CreateAccountInput(holderDocument, currency, limit));
        }

        return ReadResult.Invalid<CreateAccountInput>(issues);
    }

    private static HolderDocument? ReadDocument(JsonObjectBody body, List<ValidationIssue> issues)
    {
        var text = BodyFields.Text(body, HolderDocumentField, true, issues);

        if (text is null)
        {
            return null;
        }

        var document = HolderDocument.From(text);

        if (document.IsSuccess)
        {
            return document.Value;
        }

        issues.Add(FieldIssues.InvalidFormat(HolderDocumentField));

        return null;
    }

    private static string? ReadCurrency(JsonObjectBody body, List<ValidationIssue> issues)
    {
        var currency = BodyFields.Currency(body, CurrencyField, issues);

        if (currency is null || currency == SupportedCurrency)
        {
            return currency;
        }

        issues.Add(FieldIssues.UnsupportedCurrency(CurrencyField));

        return null;
    }

    private static Money? BuildLimit(decimal? overdraft, string? currency, List<ValidationIssue> issues)
    {
        if (currency is null)
        {
            return null;
        }

        var money = Money.Create(overdraft ?? decimal.Zero, currency);

        if (money.IsSuccess)
        {
            return money.Value;
        }

        issues.Add(FieldIssues.OutOfRange(OverdraftLimitField));

        return null;
    }
}
