namespace Ledger.Application.Entries;

public sealed record EntryOutcome(EntryView Entry, bool IsReplay);
