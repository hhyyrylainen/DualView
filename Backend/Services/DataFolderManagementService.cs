using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Backend.Services;

public class DataFolderService : IDataFolderService
{
    private readonly string appName;
    private readonly ILoggingService logger;

    public DataFolderService(string appName, ILoggingService logger)
    {
        this.appName = appName;
        this.logger = logger;
    }

    public string GetDataFolderPath()
    {
        string basePath;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // On Windows, use AppData/Local
            basePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                appName);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // On macOS, use ~/Library/Application Support
            basePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                appName);
        }
        else
        {
            // On Linux, use ~/.local/share
            basePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                appName);
        }

        return basePath;
    }

    public string GetDatabaseFilePath()
    {
        return Path.Combine(GetDataFolderPath(), "airunmanager.sqlite");
    }

    public string GetLogsFolderPath()
    {
        return Path.Combine(GetDataFolderPath(), "logs");
    }

    public void EnsureDataFoldersExist()
    {
        try
        {
            // Ensure main data folder exists
            string dataFolder = GetDataFolderPath();
            if (!Directory.Exists(dataFolder))
            {
                Directory.CreateDirectory(dataFolder);
                logger.LogInformation("Created data folder: {Folder}", dataFolder);
            }

            // Ensure logs folder exists
            string logsFolder = GetLogsFolderPath();
            if (!Directory.Exists(logsFolder))
            {
                Directory.CreateDirectory(logsFolder);
                logger.LogInformation("Created logs folder: {Folder}", logsFolder);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create data folders");
            throw;
        }
    }
}
