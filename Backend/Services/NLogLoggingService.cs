using NLog;
using Microsoft.Extensions.Logging;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Backend.Services;

public class NLogLoggingService : ILoggingService
{
    private readonly Logger logger;

    public NLogLoggingService(string loggerName)
    {
        logger = LogManager.GetLogger(loggerName);
    }

    public void Debug(string message) => logger.Debug(message);
    public void Info(string message) => logger.Info(message);
    public void Warn(string message) => logger.Warn(message);
    public void Error(string message) => logger.Error(message);
    public void Error(Exception ex, string message) => logger.Error(ex, message);
    public void Fatal(string message) => logger.Fatal(message);
    public void Fatal(Exception ex, string message) => logger.Fatal(ex, message);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        // Only log if the level is enabled
        if (!IsEnabled(logLevel))
            return;

        // Convert Microsoft LogLevel to NLog LogLevel
        var nlogLevel = ConvertLogLevel(logLevel);

        // Format the message
        string message = formatter(state, exception);

        // Log using NLog
        if (exception != null)
            // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
        {
            logger.Log(nlogLevel, exception, message);
        }
        else
        {
            logger.Log(nlogLevel, message);
        }
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        // Convert Microsoft LogLevel to NLog LogLevel and check if enabled
        var nlogLevel = ConvertLogLevel(logLevel);
        return logger.IsEnabled(nlogLevel);
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        // Create a new NLog scope using the recommended ScopeContext
        return ScopeContext.PushNestedState(state);
    }

    // Helper method to convert Microsoft LogLevels to NLog LogLevels
    private static NLog.LogLevel ConvertLogLevel(LogLevel logLevel)
    {
        return logLevel switch
        {
            LogLevel.Trace => NLog.LogLevel.Trace,
            LogLevel.Debug => NLog.LogLevel.Debug,
            LogLevel.Information => NLog.LogLevel.Info,
            LogLevel.Warning => NLog.LogLevel.Warn,
            LogLevel.Error => NLog.LogLevel.Error,
            LogLevel.Critical => NLog.LogLevel.Fatal,
            LogLevel.None => NLog.LogLevel.Off,
            _ => NLog.LogLevel.Info,
        };
    }
}
