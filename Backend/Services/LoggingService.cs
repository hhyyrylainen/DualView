using Microsoft.Extensions.Logging;

namespace Backend.Services;

public interface ILoggingService : ILogger
{
    void Debug(string message);
    void Info(string message);
    void Warn(string message);
    void Error(string message);
    void Error(Exception ex, string message);
    void Fatal(string message);
    void Fatal(Exception ex, string message);
}
