using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Backend.Services;

public class NLogLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, NLogLoggingService> loggers = new();

    public ILogger CreateLogger(string categoryName)
    {
        return loggers.GetOrAdd(categoryName, name => new NLogLoggingService(name));
    }

    public void Dispose()
    {
        loggers.Clear();
    }
}
