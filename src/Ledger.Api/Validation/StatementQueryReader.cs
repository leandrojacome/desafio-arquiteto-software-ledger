using System.Collections.Frozen;
using System.Globalization;
using System.Text.RegularExpressions;
using Ledger.Api.Reads;
using Ledger.Application.Abstractions;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Microsoft.Extensions.Options;

namespace Ledger.Api.Validation;

internal sealed partial class StatementQueryReader(
    IStatementCursorProtector cursorProtector,
    IOptions<StatementOptions> options)
{
    public const string FromName = "from";
    public const string ToName = "to";
    public const string LimitName = "limit";
    public const string CursorName = "cursor";
    public const int MaxCursorLength = 64;

    private const int MaxLimitDigits = 9;
    private const string RepeatedMessage = "O parâmetro foi enviado mais de uma vez.";

    private static readonly FrozenSet<string> KnownNames =
        FrozenSet.ToFrozenSet([FromName, ToName, LimitName, CursorName], StringComparer.Ordinal);

    public ReadResult<StatementInput> Read(AccountId accountId, string? queryString)
    {
        var settings = options.Value;
        var arguments = QueryStringReader.Read(queryString, KnownNames);
        var issues = new List<ValidationIssue>(arguments.UnknownFields);

        var from = ReadInstant(arguments, FromName, issues);
        var to = ReadInstant(arguments, ToName, issues);

        if (from is { } start && to is { } end && start >= end)
        {
            issues.Add(new ValidationIssue(FromName, ValidationReasons.FromAfterTo, "O campo 'from' deve ser anterior a 'to'."));
        }

        var limit = ReadLimit(arguments, settings, issues);
        var cursor = ReadCursor(arguments, accountId, issues);

        return issues.Count == 0
            ? ReadResult.Valid(new StatementInput(from, to, limit, cursor))
            : ReadResult.Invalid<StatementInput>(issues);
    }

    private static DateTimeOffset? ReadInstant(QueryArguments arguments, string name, List<ValidationIssue> issues)
    {
        if (arguments.IsRepeated(name))
        {
            issues.Add(Repeated(name));

            return null;
        }

        var text = arguments.ValueOf(name);

        if (text is null)
        {
            return null;
        }

        var failure = InstantParameterReader.Read(text, out var instant);

        if (failure == InstantFailure.None)
        {
            return instant;
        }

        issues.Add(InstantIssues.For(name, failure));

        return null;
    }

    private static int ReadLimit(QueryArguments arguments, StatementOptions settings, List<ValidationIssue> issues)
    {
        if (arguments.IsRepeated(LimitName))
        {
            issues.Add(Repeated(LimitName));

            return settings.DefaultLimit;
        }

        var text = arguments.ValueOf(LimitName);

        if (text is null)
        {
            return settings.DefaultLimit;
        }

        if (!IntegerGrammar().IsMatch(text))
        {
            issues.Add(new ValidationIssue(LimitName, ValidationReasons.InvalidFormat, "O campo 'limit' deve ser um número inteiro."));

            return settings.DefaultLimit;
        }

        if (TryParseLimit(text, settings.MaxLimit, out var limit))
        {
            return limit;
        }

        issues.Add(new ValidationIssue(
            LimitName,
            ValidationReasons.OutOfRange,
            string.Create(CultureInfo.InvariantCulture, $"O campo 'limit' deve estar entre 1 e {settings.MaxLimit}.")));

        return settings.DefaultLimit;
    }

    private static bool TryParseLimit(string text, int maxLimit, out int limit)
    {
        limit = 0;

        if (text.TrimStart('-').Length > MaxLimitDigits)
        {
            return false;
        }

        limit = int.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

        return limit >= 1 && limit <= maxLimit;
    }

    private StatementPosition? ReadCursor(QueryArguments arguments, AccountId accountId, List<ValidationIssue> issues)
    {
        if (arguments.IsRepeated(CursorName))
        {
            issues.Add(InvalidCursor());

            return null;
        }

        var text = arguments.ValueOf(CursorName);

        if (text is null)
        {
            return null;
        }

        if (text.Length is 0 or > MaxCursorLength)
        {
            issues.Add(InvalidCursor());

            return null;
        }

        var position = cursorProtector.Unprotect(accountId, text);

        if (position.IsFailure)
        {
            issues.Add(InvalidCursor());

            return null;
        }

        return position.Value;
    }

    private static ValidationIssue Repeated(string name) =>
        new(name, ValidationReasons.InvalidFormat, RepeatedMessage);

    private static ValidationIssue InvalidCursor() =>
        new(CursorName, ValidationReasons.InvalidCursor, StatementErrors.InvalidCursor.Message);

    [GeneratedRegex(@"^-?[0-9]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex IntegerGrammar();
}
