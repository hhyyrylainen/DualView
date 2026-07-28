using DualView.Shared.Models.Enums;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace Backend.Services;

public static class NLogConfigurator
{
    public static void Configure(string logDirectory, AppComponent appComponent)
    {
        if (appComponent == AppComponent.WebUI)
            throw new Exception("NLog configuration should not be done for WebUI.");

        var config = new LoggingConfiguration();

        string namePrefix;
        switch (appComponent)
        {
            case AppComponent.DesktopGUI:
                namePrefix = "gui-";
                break;
            case AppComponent.Server:
                namePrefix = "server-";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(appComponent), appComponent, null);
        }

        // Create a console target
        var consoleTarget = new ColoredConsoleTarget("console")
        {
            Layout =
                "${longdate}|${level:uppercase=true}|${logger}|${scopenested}|${message} ${exception:format=tostring}",
        };
        config.AddTarget(consoleTarget);

        // Create the file target for all logs
        var fileTarget = new AtomicFileTarget
        {
            Name = "file",
            FileName = Path.Combine(logDirectory, namePrefix + "${shortdate}.log"),
            Layout =
                "${longdate}|${level:uppercase=true}|${logger}|${scopenested}|${message} ${exception:format=tostring}",
            MaxArchiveFiles = 7,
            ArchiveEvery = FileArchivePeriod.Day,
            ConcurrentWrites = true,
            EnableFileDelete = true,
        };
        config.AddTarget(fileTarget);

        // Create the file target for error logs
        var errorFileTarget = new AtomicFileTarget
        {
            Name = "errorFile",
            FileName = Path.Combine(logDirectory, namePrefix + "error-${shortdate}.log"),
            Layout =
                "${longdate}|${level:uppercase=true}|${logger}|${scopenested}|${message} ${exception:format=tostring}",
            // ArchiveFileName = Path.Combine(logDirectory, "archives", "error-{#}.log"),
            // ArchiveNumbering = ArchiveNumberingMode.Date,
            ArchiveEvery = FileArchivePeriod.Day,
            MaxArchiveFiles = 14,
            ConcurrentWrites = true,
            EnableFileDelete = true,
        };
        config.AddTarget(errorFileTarget);

        // Add rules
        config.AddRule(LogLevel.Trace, LogLevel.Fatal, consoleTarget);

        // TODO: option for more verbose logging
        // config.AddRule(LogLevel.Debug, LogLevel.Fatal, fileTarget);
        config.AddRule(LogLevel.Info, LogLevel.Fatal, fileTarget);
        config.AddRule(LogLevel.Error, LogLevel.Fatal, errorFileTarget);

        // Apply config
        LogManager.Configuration = config;
    }
}
