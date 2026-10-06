using Ledger.Domain.Shared;

namespace Ledger.Application.Entries;

public sealed record AppliedEntry(EntryView Entry, bool RecordedAtCorrected);

public static class ApplyErrors
{
    public static readonly Error NotMatched =
        new("ENTRY_NOT_APPLIED",
            "A instrução condicional não encontrou nenhuma linha de conta correspondente.",
            ErrorKind.Unprocessable);
}

public sealed record EntryOutcome(EntryView Entry, bool IsReplay);

internal sealed record WrittenEntry(EntryView Entry, bool IsReplay, bool RecordedAtCorrected);
