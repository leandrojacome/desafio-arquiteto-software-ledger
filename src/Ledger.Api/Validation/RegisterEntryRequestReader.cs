using System.Collections.Frozen;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;
using Microsoft.Extensions.Options;

namespace Ledger.Api.Validation;

internal sealed class RegisterEntryRequestReader(TimeProvider time, IOptions<LedgerOptions> options)
{
    private const string TypeField = "type";
    private const string AmountField = "amount";
    private const string CurrencyField = "currency";
    private const string OccurredAtField = "occurredAt";
    private const string DescriptionField = "description";
    private const string ReferenceField = "reference";

    private static readonly FrozenSet<string> KnownFields = new[]
    {
        TypeField, AmountField, CurrencyField, OccurredAtField, DescriptionField, ReferenceField
    }.ToFrozenSet(StringComparer.Ordinal);

    public ReadResult<RegisterEntryInput> Read(ReadOnlySpan<byte> body)
    {
        using var parsed = JsonObjectBody.TryParse(body, out var rootIssue);

        if (parsed is null)
        {
            return ReadResult.Invalid<RegisterEntryInput>([rootIssue ?? FieldIssues.InvalidJson()]);
        }

        var issues = new List<ValidationIssue>();

        var type = ReadType(parsed, issues);
        var amount = BodyFields.Amount(parsed, AmountField, true, false, issues);
        var currency = BodyFields.Currency(parsed, CurrencyField, issues);
        var occurredAt = BodyFields.Instant(
            parsed,
            OccurredAtField,
            time,
            options.Value.OccurredAtFutureToleranceMinutes,
            issues);
        var description = BodyFields.Description(parsed, DescriptionField, issues);
        var reference = BodyFields.Reference(parsed, ReferenceField, issues);

        issues.AddRange(parsed.UnknownFields(KnownFields));

        var money = BuildMoney(amount, currency, issues);

        if (issues.Count == 0 && type is { } entryType && money is not null)
        {
            return ReadResult.Valid(new RegisterEntryInput(entryType, money, occurredAt, description, reference));
        }

        return ReadResult.Invalid<RegisterEntryInput>(issues);
    }

    private static EntryType? ReadType(JsonObjectBody body, List<ValidationIssue> issues)
    {
        var text = BodyFields.Text(body, TypeField, true, issues);

        if (text is null)
        {
            return null;
        }

        if (EntryTypeText.TryParse(text, out var type))
        {
            return type;
        }

        issues.Add(FieldIssues.NotAllowed(TypeField));

        return null;
    }

    private static Money? BuildMoney(decimal? amount, string? currency, List<ValidationIssue> issues)
    {
        if (amount is not { } value || currency is null)
        {
            return null;
        }

        var money = Money.CreatePositive(value, currency);

        if (money.IsSuccess)
        {
            return money.Value;
        }

        issues.Add(FieldIssues.OutOfRange(AmountField));

        return null;
    }
}
