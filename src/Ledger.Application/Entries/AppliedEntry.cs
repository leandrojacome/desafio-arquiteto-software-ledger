namespace Ledger.Application.Entries;

public sealed record AppliedEntry(EntryView Entry, bool RecordedAtCorrected);
