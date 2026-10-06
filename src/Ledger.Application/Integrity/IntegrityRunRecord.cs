namespace Ledger.Application.Integrity;

public sealed record IntegrityRunRecord(
    DateTimeOffset RecordedAt,
    string Outcome,
    IntegrityMode Mode,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd);
