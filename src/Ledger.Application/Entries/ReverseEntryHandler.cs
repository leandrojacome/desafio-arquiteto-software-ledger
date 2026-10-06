using Ledger.Application.Abstractions;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;
using Microsoft.Extensions.Logging;

namespace Ledger.Application.Entries;

public sealed class ReverseEntryHandler(
    IUnitOfWork unitOfWork,
    IIdGenerator idGenerator,
    IEntryTelemetry telemetry,
    ILogger<ReverseEntryHandler> logger)
{
    public async Task<Result<EntryOutcome>> HandleAsync(
        ReverseEntryCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var operation = telemetry.Begin(EntryReporting.ReversalLabel);

        var context = WriteContext.Create(
            idGenerator,
            command.AccountId,
            command.IdempotencyKey,
            CanonicalRequestHash.ForReversal(command),
            command.ClientId,
            command.CorrelationId,
            command.TraceParent);

        var result = await unitOfWork.ExecuteAsync(
            (scope, token) => EntryWriteFlow.RunAsync(
                scope,
                context,
                (innerScope, innerToken) => PrepareAsync(innerScope, command, context, innerToken),
                token),
            cancellationToken);

        EntryReporting.Report(
            logger,
            operation,
            context,
            EntryReporting.ReversalLabel,
            EntryReporting.ReverseOperation,
            result);

        return EntryReporting.ToOutcome(result);
    }

    private static async Task<Result<Entry>> PrepareAsync(
        IUnitOfWorkScope scope,
        ReverseEntryCommand command,
        WriteContext context,
        CancellationToken cancellationToken)
    {
        var original = await scope.Entries.FindForReversalAsync(
            command.AccountId,
            command.OriginalEntryId,
            cancellationToken);

        if (original is null)
        {
            return EntryErrors.NotFound;
        }

        var plan = original.Plan();

        if (plan.IsFailure)
        {
            return plan.Error;
        }

        return Entry.ReversalOf(plan.Value, context.EntryId, command.AccountId, command.Description);
    }
}
