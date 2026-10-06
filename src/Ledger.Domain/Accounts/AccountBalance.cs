using System.Text;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;

namespace Ledger.Domain.Accounts;

public sealed record AccountBalance
{
    private AccountBalance(AccountId accountId, string currency, decimal amount, decimal overdraftLimit)
    {
        AccountId = accountId;
        Currency = currency;
        Amount = amount;
        OverdraftLimit = overdraftLimit;
    }

    public AccountId AccountId { get; }

    public string Currency { get; }

    public decimal Amount { get; }

    public decimal OverdraftLimit { get; }

    public static Result<AccountBalance> Create(
        AccountId accountId,
        string currency,
        decimal amount,
        decimal overdraftLimit)
    {
        var limit = Money.Create(overdraftLimit, currency);

        if (limit.IsFailure)
        {
            return limit.Error;
        }

        var balance = Money.Create(amount, currency);

        if (balance.IsFailure)
        {
            return balance.Error;
        }

        if (overdraftLimit < decimal.Zero)
        {
            return AccountErrors.InvalidOverdraftLimit;
        }

        if (!Fits(amount, overdraftLimit))
        {
            return AccountErrors.BalanceBelowOverdraftLimit;
        }

        return new AccountBalance(accountId, currency, amount, overdraftLimit);
    }

    public Result<AccountBalance> Apply(EntryType type, Money amount)
    {
        if (!amount.IsPositive)
        {
            return MoneyErrors.MustBePositive;
        }

        if (amount.Currency != Currency)
        {
            return AccountErrors.CurrencyMismatch;
        }

        var next = Amount + type.SignedDelta(amount);

        if (!Fits(next, OverdraftLimit))
        {
            return EntryErrors.InsufficientFunds;
        }

        if (next > Money.MaxAbsoluteAmount)
        {
            return MoneyErrors.OutOfRange;
        }

        return new AccountBalance(AccountId, Currency, next, OverdraftLimit);
    }

    private static bool Fits(decimal balance, decimal overdraftLimit) => balance >= -overdraftLimit;

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("AccountId = ").Append(AccountId).Append(", Currency = ").Append(Currency);

        return true;
    }
}
