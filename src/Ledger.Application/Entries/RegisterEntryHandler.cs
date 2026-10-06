using Ledger.Application.Abstractions;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;
using Microsoft.Extensions.Logging;

namespace Ledger.Application.Entries;

public sealed class RegisterEntryHandler(
    IUnitOfWork unitOfWork,
    IIdGenerator idGenerator,
    IEntryTelemetry telemetry,
    ILogger<RegisterEntryHandler> logger)
{
    public async Task<Result<EntryOutcome>> HandleAsync(
        RegisterEntryCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var typeLabel = EntryReporting.LabelOf(command.Type);
        using var operation = telemetry.Begin(typeLabel);

        var context = WriteContext.Create(
            idGenerator,
            command.AccountId,
            command.IdempotencyKey,
            CanonicalRequestHash.ForRegistration(command),
            command.ClientId,
            command.CorrelationId,
            command.TraceParent);

        var result = await unitOfWork.ExecuteAsync(
            (scope, token) => EntryWriteFlow.RunAsync(
                scope,
                context,
                (_, _) => Task.FromResult(Build(command, context)),
                token),
            cancellationToken);

        EntryReporting.Report(logger, operation, context, typeLabel, EntryReporting.RegisterOperation, result);

        return EntryReporting.ToOutcome(result);
    }

    private static Result<Entry> Build(RegisterEntryCommand command, WriteContext context)
    {
        return command.Type == EntryType.Credit
            ? Entry.Credit(
                context.EntryId,
                command.AccountId,
                command.Amount,
                command.OccurredAt,
                command.Description,
                command.Reference)
            : Entry.Debit(
                context.EntryId,
                command.AccountId,
                command.Amount,
                command.OccurredAt,
                command.Description,
                command.Reference);
    }
}
