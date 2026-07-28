using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backend.Services;

public interface ITemporaryFolderService
{
    public Task<string> GetTemporaryFolder();
}

public class TemporaryFolderService : ITemporaryFolderService, IDisposable
{
    private static bool clearedTemporaryFolder;

    private static readonly Lock ClearLock = new();

    private readonly ILogger<TemporaryFolderService> logger;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly IAppEvents appEvents;
    private readonly IDataFolderService dataFolderService;

    private readonly SemaphoreSlim settingsSemaphore = new(1, 1);

    private bool settingsDirty = true;

    private string? tempFolder;

    public TemporaryFolderService(ILogger<TemporaryFolderService> logger, IServiceScopeFactory scopeFactory,
        IAppEvents appEvents, IDataFolderService dataFolderService)
    {
        this.logger = logger;
        this.scopeFactory = scopeFactory;
        this.appEvents = appEvents;
        this.dataFolderService = dataFolderService;

        appEvents.SettingsChanged += OnInvalidateSettings;
    }

    public async Task<string> GetTemporaryFolder()
    {
        await ReloadSettings();

        var folder = tempFolder ?? throw new Exception("Could not load temporary folder settings");

        if (!clearedTemporaryFolder)
        {
            try
            {
                lock (ClearLock)
                {
                    if (!clearedTemporaryFolder)
                    {
                        logger.LogInformation("Clearing temporary folder: {Folder}", folder);
                        Directory.Delete(folder, true);
                        clearedTemporaryFolder = true;
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to clear temporary folder");
            }
        }

        if (!Directory.Exists(folder))
        {
            logger.LogInformation("Temporary folder does not exist, creating: {Folder}", folder);
            Directory.CreateDirectory(folder);
        }

        return folder;
    }

    public void Dispose()
    {
        appEvents.SettingsChanged -= OnInvalidateSettings;
        settingsSemaphore.Dispose();
    }

    private void OnInvalidateSettings()
    {
        settingsDirty = true;
    }

    private async Task ReloadSettings()
    {
        if (!settingsDirty && tempFolder != null)
            return;

        await settingsSemaphore.WaitAsync();
        try
        {
            using var scope = scopeFactory.CreateScope();
            using var database = scope.ServiceProvider.GetRequiredService<IDatabaseService>();

            settingsDirty = false;
            var newSettings = await database.GetAppSettingsAsync();

            var baseFolder = newSettings.LocalMediaStorageLocation;

            if (string.IsNullOrEmpty(baseFolder))
                baseFolder = dataFolderService.GetDataFolderPath();

            var newFolder = Path.Combine(baseFolder, "temp");

            if (newFolder != tempFolder)
            {
                tempFolder = newFolder;
                logger.LogInformation("Refreshed settings for new media temp folder location");

                // TODO: deleting old data folder?
            }
        }
        finally
        {
            settingsSemaphore.Release();
        }
    }
}
