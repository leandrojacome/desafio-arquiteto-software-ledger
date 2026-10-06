using Ledger.Domain.Entries;

namespace Ledger.Api.Validation;

internal static class IdempotencyKeyReader
{
    public const string HeaderName = "Idempotency-Key";

    public static IdempotencyKeyReading Read(IHeaderDictionary headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        var values = headers[HeaderName];

        if (values.Count == 0)
        {
            return IdempotencyKeyReading.Missing;
        }

        if (values.Count > 1)
        {
            return IdempotencyKeyReading.Rejected(new ValidationIssue(
                HeaderName,
                ValidationReasons.InvalidFormat,
                $"O cabeçalho '{HeaderName}' deve ser enviado uma única vez."));
        }

        var text = values[0];
        var key = IdempotencyKey.From(text);

        if (key.IsSuccess)
        {
            return IdempotencyKeyReading.Accepted(key.Value);
        }

        if (string.IsNullOrEmpty(text))
        {
            return IdempotencyKeyReading.Missing;
        }

        return IdempotencyKeyReading.Rejected(text.Length > IdempotencyKey.MaxLength
            ? FieldIssues.TooLong(HeaderName, IdempotencyKey.MaxLength)
            : FieldIssues.InvalidFormat(HeaderName));
    }
}
