using Ledger.Application.Abstractions;
using Ledger.Domain.Accounts;
using Ledger.Domain.Shared;

namespace Ledger.Application.Balances;

public sealed class GetBalanceHandler(
    IBalanceReader reader,
    BalanceReadSettings settings,
    IReadTelemetry telemetry,
    ReadAudit audit)
{
    private const string CurrentTelemetryMode = "current";
    private const string AsOfTelemetryMode = "as_of";
    private const string CurrentAuditMode = "CURRENT";
    private const string AsOfAuditMode = "AS_OF";

    public async Task<Result<BalanceView>> HandleAsync(GetBalanceQuery query, CancellationToken cancellationToken)
    {
        var requestedAsOf = query.AsOf?.ToUniversalTime();
        var isHistorical = requestedAsOf is not null;

        using var operation = telemetry.BeginBalance(isHistorical ? AsOfTelemetryMode : CurrentTelemetryMode);

        var result = requestedAsOf is { } asOf
            ? await ReadAtAsync(query.AccountId, asOf, cancellationToken)
            : await ReadCurrentAsync(query.AccountId, cancellationToken);

        if (result.IsSuccess)
        {
            audit.BalanceQueried(
                query.ClientId,
                query.AccountId,
                query.CorrelationId,
                isHistorical ? AsOfAuditMode : CurrentAuditMode,
                requestedAsOf);
        }

        return result;
    }

    private static Money RequireMoney(decimal amount, string currency)
    {
        var money = Money.Create(amount, currency);

        return money.IsSuccess
            ? money.Value
            : throw new InvalidOperationException("The stored balance carries an invalid amount or currency.");
    }

    private async Task<Result<BalanceView>> ReadCurrentAsync(AccountId accountId, CancellationToken cancellationToken)
    {
        var reading = await reader.ReadCurrentAsync(accountId, cancellationToken);

        if (reading.IsFailure)
        {
            return reading.Error;
        }

        var current = reading.Value;

        return new BalanceView(
            accountId,
            RequireMoney(current.Balance, current.Currency),
            RequireMoney(current.OverdraftLimit, current.Currency),
            current.LastEntryId,
            current.DatabaseNow,
            null);
    }

    private async Task<Result<BalanceView>> ReadAtAsync(
        AccountId accountId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        var reading = await reader.ReadAtAsync(accountId, asOf, cancellationToken);

        if (reading.IsFailure)
        {
            return reading.Error;
        }

        var historical = reading.Value;

        if (asOf > historical.DatabaseNow)
        {
            return BalanceErrors.AsOfInTheFuture;
        }

        return new BalanceView(
            accountId,
            RequireMoney(historical.BalanceAfter ?? decimal.Zero, historical.Currency),
            RequireMoney(historical.OverdraftLimit, historical.Currency),
            historical.LastEntryId,
            asOf,
            asOf <= historical.DatabaseNow - settings.SettlingWindow);
    }
}
