using Microsoft.Extensions.Logging;

namespace Ledger.Application.Tests.Support;

internal sealed class CapturingLogger<T> : CapturingLogger, ILogger<T>
{
}
