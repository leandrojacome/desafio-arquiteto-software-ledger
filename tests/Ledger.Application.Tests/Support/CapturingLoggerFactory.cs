using Microsoft.Extensions.Logging;

namespace Ledger.Application.Tests.Support;

internal sealed class CapturingLoggerFactory : ILoggerFactory
{
    private readonly Dictionary<string, CapturingLogger> _loggers = [];

    public IReadOnlyDictionary<string, CapturingLogger> Loggers => _loggers;

    public CapturingLogger For(string category) => _loggers[category];

    public ILogger CreateLogger(string categoryName)
    {
        if (!_loggers.TryGetValue(categoryName, out var logger))
        {
            logger = new CapturingLogger();
            _loggers[categoryName] = logger;
        }

        return logger;
    }

    public void AddProvider(ILoggerProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
    }

    public void Dispose()
    {
    }
}
