using System.Data.Common;
using Ledger.Application.Entries;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;

namespace Ledger.Infrastructure.Persistence;

internal static class RowMapping
{
    public const int EntryColumns = 12;

    public static async Task<EntryView> ReadEntryAsync(
        DbDataReader reader,
        int offset,
        CancellationToken cancellationToken)
    {
        var currency = reader.GetString(offset + 5);

        return new EntryView(
            RequireEntryId(reader.GetGuid(offset)),
            RequireAccountId(reader.GetGuid(offset + 1)),
            reader.GetInt64(offset + 2),
            RequireType(reader.GetString(offset + 3)),
            RequireMoney(reader.GetDecimal(offset + 4), currency),
            RequireMoney(reader.GetDecimal(offset + 6), currency),
            await reader.GetFieldValueAsync<DateTimeOffset>(offset + 7, cancellationToken),
            await reader.GetFieldValueAsync<DateTimeOffset>(offset + 8, cancellationToken),
            await ReadTextAsync(reader, offset + 9, cancellationToken),
            await ReadTextAsync(reader, offset + 10, cancellationToken),
            await ReadOptionalEntryIdAsync(reader, offset + 11, cancellationToken));
    }

    public static async Task<EntryView> ReadStatementEntryAsync(
        DbDataReader reader,
        AccountId accountId,
        CancellationToken cancellationToken)
    {
        var currency = reader.GetString(4);

        return new EntryView(
            RequireEntryId(reader.GetGuid(0)),
            accountId,
            reader.GetInt64(1),
            RequireType(reader.GetString(2)),
            RequireMoney(reader.GetDecimal(3), currency),
            RequireMoney(reader.GetDecimal(5), currency),
            await reader.GetFieldValueAsync<DateTimeOffset>(6, cancellationToken),
            await reader.GetFieldValueAsync<DateTimeOffset>(7, cancellationToken),
            await ReadTextAsync(reader, 8, cancellationToken),
            await ReadTextAsync(reader, 9, cancellationToken),
            await ReadOptionalEntryIdAsync(reader, 10, cancellationToken));
    }

    public static EntryId RequireEntryId(Guid value)
    {
        var id = EntryId.From(value);

        return id.IsSuccess ? id.Value : throw new InvalidOperationException("The stored entry id is empty.");
    }

    public static AccountId RequireAccountId(Guid value)
    {
        var id = AccountId.From(value);

        return id.IsSuccess ? id.Value : throw new InvalidOperationException("The stored account id is empty.");
    }

    public static EntryType RequireType(string text)
    {
        return EntryTypeText.TryParse(text, out var type)
            ? type
            : throw new InvalidOperationException("The stored entry type is neither CREDIT nor DEBIT.");
    }

    public static Money RequireMoney(decimal amount, string currency)
    {
        var money = Money.Create(amount, currency);

        return money.IsSuccess
            ? money.Value
            : throw new InvalidOperationException("The stored amount or currency is invalid.");
    }

    public static async Task<string?> ReadTextAsync(
        DbDataReader reader,
        int ordinal,
        CancellationToken cancellationToken) =>
        await reader.IsDBNullAsync(ordinal, cancellationToken) ? null : reader.GetString(ordinal);

    public static async Task<EntryId?> ReadOptionalEntryIdAsync(
        DbDataReader reader,
        int ordinal,
        CancellationToken cancellationToken) =>
        await reader.IsDBNullAsync(ordinal, cancellationToken) ? null : RequireEntryId(reader.GetGuid(ordinal));
}
