using Ledger.Application.Abstractions;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;
using Microsoft.Extensions.Logging;

namespace Ledger.Application.Entries;

internal static class EntryReporting
{
    public const string CreditLabel = "credit";
    public const string DebitLabel = "debit";
    public const string ReversalLabel = "reversal";

    public const string RegisterOperation = "register";
    public const string ReverseOperation = "reverse";

    private const string ValidationReason = "validation";

    public static string LabelOf(EntryType type) =>
        type == EntryType.Credit ? CreditLabel : DebitLabel;

    public static Result<EntryOutcome> ToOutcome(Result<WrittenEntry> result) =>
        result.IsSuccess ? new EntryOutcome(result.Value.Entry, result.Value.IsReplay) : result.Error;

    public static void Report(
        ILogger logger,
        IEntryOperation operation,
        WriteContext context,
        string typeLabel,
        string operationName,
        Result<WrittenEntry> result)
    {
        if (result.IsSuccess)
        {
            ReportSuccess(logger, operation, context, typeLabel, result.Value);
            return;
        }

        ReportFailure(logger, operation, context, operationName, result.Error);
    }

    private static void ReportSuccess(
        ILogger logger,
        IEntryOperation operation,
        WriteContext context,
        string typeLabel,
        WrittenEntry written)
    {
        if (written.IsReplay)
        {
            operation.Replayed();
            LogReplay(logger, context, written.Entry.Id);
            return;
        }

        operation.Recorded(typeLabel);
        EntryLog.EntryAccepted(logger, written.Entry.Id, context.AccountId, context.ClientId);

        if (written.RecordedAtCorrected)
        {
            operation.RecordedAtCorrected();
            EntryLog.RecordedAtCorrected(logger, written.Entry.Id, context.AccountId, context.ClientId);
        }
    }

    private static void ReportFailure(
        ILogger logger,
        IEntryOperation operation,
        WriteContext context,
        string operationName,
        Error error)
    {
        if (error == EntryErrors.IdempotencyKeyReused)
        {
            operation.IdempotencyConflict();
            LogConflict(logger, context, operationName);
            return;
        }

        operation.Rejected(ReasonOf(error));

        if (error == EntryErrors.InsufficientFunds)
        {
            LogInsufficientFunds(logger, context);
        }
    }

    private static void LogReplay(ILogger logger, WriteContext context, EntryId entryId)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            var fingerprint = context.Key.Fingerprint;
            EntryLog.IdempotentReplayDelivered(logger, entryId, context.AccountId, context.ClientId, fingerprint);
        }
    }

    private static void LogConflict(ILogger logger, WriteContext context, string operationName)
    {
        if (logger.IsEnabled(LogLevel.Warning))
        {
            var fingerprint = context.Key.Fingerprint;
            EntryLog.IdempotencyKeyReused(logger, fingerprint, context.AccountId, context.ClientId, operationName);
        }
    }

    private static void LogInsufficientFunds(ILogger logger, WriteContext context)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            var fingerprint = context.Key.Fingerprint;
            EntryLog.InsufficientFundsRejected(logger, context.AccountId, context.ClientId, fingerprint);
        }
    }

    private static string ReasonOf(Error error)
    {
        if (error == EntryErrors.InsufficientFunds)
        {
            return "insufficient_funds";
        }

        if (error == AccountErrors.CurrencyMismatch)
        {
            return "currency_mismatch";
        }

        if (error == AccountErrors.NotFound)
        {
            return "account_not_found";
        }

        if (error == EntryErrors.AlreadyReversed)
        {
            return "entry_already_reversed";
        }

        if (error == EntryErrors.NotReversible)
        {
            return "entry_not_reversible";
        }

        if (error == EntryErrors.NotFound)
        {
            return "entry_not_found";
        }

        return ValidationReason;
    }
}
