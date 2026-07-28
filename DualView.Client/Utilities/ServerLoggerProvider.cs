using System.Net.Http.Json;
using DualView.Shared.Requests;

namespace DualView.Client.Utilities;

public class ServerLoggerProvider : ILoggerProvider
{
    private readonly HttpClient httpClient;

    public ServerLoggerProvider(string baseAddress)
    {
        // We use a dedicated HttpClient for logging to avoid dependency loops
        httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseAddress),
            Timeout = TimeSpan.FromSeconds(15),
        };
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new ServerLogger(categoryName, httpClient);
    }

    public void Dispose()
    {
        httpClient.Dispose();
    }

    private class ServerLogger : ILogger
    {
        private readonly string categoryName;
        private readonly HttpClient httpClient;

        public ServerLogger(string categoryName, HttpClient httpClient)
        {
            this.categoryName = categoryName;
            this.httpClient = httpClient;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel)
        {
            // Only listen to Warning and above
            return logLevel >= LogLevel.Warning;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            // CRITICAL: Ignore logs from HttpClient to prevent infinite loops
            // (If sending a log fails, it logs an error, which we try to send, which fails...)
            if (categoryName.StartsWith("System.Net.Http"))
                return;

            try
            {
                var message = formatter(state, exception);

                var logModel = new LogForwardRequest
                {
                    Level = logLevel.ToString(),
                    Message = $"{categoryName}|{message}",
                    Exception = exception?.ToString(),
                };

                // TODO: shouldn't this be waited because otherwise the exception is not caught?
                // Maybe with Task.Run?
                httpClient.PostAsJsonAsync("api/v1/logs", logModel).ConfigureAwait(false);
            }
            catch(Exception e)
            {
                // Swallow errors if logging fails to not recursively call this
                // But write to the browser console for debugging help
                Console.WriteLine(e);
            }
        }
    }
}
