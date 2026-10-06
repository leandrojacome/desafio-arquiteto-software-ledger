using System.Globalization;
using Ledger.Domain.Accounts;

namespace Ledger.Api.IntegrationTests.Security;

internal static class CpfFactory
{
    private const int FirstBody = 123_456_001;

    public static HolderDocument Document(int index)
    {
        var body = (FirstBody + (index * 7919)).ToString("D9", CultureInfo.InvariantCulture);

        if (body.Distinct().Count() == 1)
        {
            body = (FirstBody + (index * 7919) + 1).ToString("D9", CultureInfo.InvariantCulture);
        }

        var digits = body.Select(character => character - '0').ToList();
        digits.Add(CheckDigit(digits, 10));
        digits.Add(CheckDigit(digits, 11));

        var text = string.Concat(digits.Select(digit => digit.ToString(CultureInfo.InvariantCulture)));
        var document = HolderDocument.From(text);

        return document.IsSuccess
            ? document.Value
            : throw new InvalidOperationException("The generated CPF was not accepted by the domain.");
    }

    private static int CheckDigit(List<int> digits, int firstWeight)
    {
        var sum = 0;

        for (var position = 0; position < digits.Count; position++)
        {
            sum += digits[position] * (firstWeight - position);
        }

        var remainder = sum * 10 % 11;

        return remainder == 10 ? 0 : remainder;
    }
}
