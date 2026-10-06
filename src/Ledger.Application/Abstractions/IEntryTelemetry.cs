namespace Ledger.Application.Abstractions;

public interface IEntryTelemetry
{
    IEntryOperation Begin(string operation);
}
