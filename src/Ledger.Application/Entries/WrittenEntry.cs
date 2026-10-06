namespace Ledger.Application.Entries;

internal sealed record WrittenEntry(EntryView Entry, bool IsReplay, bool RecordedAtCorrected);
