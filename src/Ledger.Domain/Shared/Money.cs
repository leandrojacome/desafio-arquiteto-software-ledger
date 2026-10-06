using System.Globalization;
using System.Text;

namespace Ledger.Domain.Shared;

public sealed record Money
{
    private const int DecimalPlaces = 2;

    public const decimal MaxAbsoluteAmount = 9_999_999_999_999_999.99m;

    private const int CurrencyLength = 3;

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public bool IsPositive => Amount > decimal.Zero;

    public static Result<Money> Create(decimal amount, string currency)
    {
        if (!IsValidCurrency(currency))
        {
            return MoneyErrors.InvalidCurrency;
        }

        if (decimal.Round(amount, DecimalPlaces) != amount)
        {
            return MoneyErrors.TooManyDecimals;
        }

        if (Math.Abs(amount) > MaxAbsoluteAmount)
        {
            return MoneyErrors.OutOfRange;
        }

        return new Money(decimal.Round(amount, DecimalPlaces), currency);
    }

    public static Result<Money> CreatePositive(decimal amount, string currency)
    {
        var created = Create(amount, currency);

        if (created.IsFailure)
        {
            return created;
        }

        return created.Value.IsPositive ? created : MoneyErrors.MustBePositive;
    }

    public string ToDecimalString() => Amount.ToString("F2", CultureInfo.InvariantCulture);

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Currency = ").Append(Currency);

        return true;
    }

    private static bool IsValidCurrency(string? currency) =>
        currency is { Length: CurrencyLength } && currency.All(char.IsAsciiLetterUpper);
}
