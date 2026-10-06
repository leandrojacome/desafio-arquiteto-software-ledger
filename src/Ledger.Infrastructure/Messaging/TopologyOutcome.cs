namespace Ledger.Infrastructure.Messaging;

internal enum TopologyOutcome
{
    Declared = 1,
    RetentionDisabled = 2,
    RetentionKeptAsFound = 3
}
