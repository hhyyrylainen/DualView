using System.Text;
using DualView.Shared.Models;
using DualView.Shared.Models.DTO;
using DualView.Shared.Models.Enums;
using DualView.Shared.Services;
using DualView.Shared.Utils;
using Backend.Database;
using Backend.Models;
using Backend.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Services;

public class DatabaseService : IDatabaseService, IClientDatabaseService
{
    private readonly ILogger<DatabaseService> logger;
    private readonly AppDbContext dbContext;
    private readonly IEntityUpdateNotifier updateNotifier;
    private readonly IAppEvents appEvents;

    private readonly TimeSpan oldChatThreshold = TimeSpan.FromHours(24);

    private bool disposed;

    public DatabaseService(ILogger<DatabaseService> logger, AppDbContext dbContext,
        IEntityUpdateNotifier updateNotifier, IAppEvents appEvents)
    {
        this.logger = logger;
        this.dbContext = dbContext;
        this.updateNotifier = updateNotifier;
        this.appEvents = appEvents;
    }

    public async Task InitializeDatabaseAsync()
    {
        try
        {
            logger.LogInformation("Initializing database...");
            await dbContext.Database.MigrateAsync();
            logger.LogInformation("Database migration completed successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize database");
            throw;
        }

        // Initialize settings if missing
        var settings = await dbContext.AppSettings.FindAsync(1);

        if (settings == null)
        {
            logger.LogInformation("Settings not found, initializing...");

            // Initialize settings
            settings = new DualViewSettings
            {
                Id = 1,
            };

            await dbContext.AppSettings.AddAsync(settings);
            await dbContext.SaveChangesAsync();
        }
    }

    public async Task<DualViewSettings> GetAppSettingsAsync()
    {
        return await dbContext.AppSettings.FindAsync(1) ?? throw new InvalidOperationException("Settings not found");
    }

    /// <summary>
    ///   Save all changes to the database for modified entities.
    /// </summary>
    /// <returns></returns>
    public Task SaveAsync()
    {
        logger.LogDebug("Saving changes to database (without notifications)");
        return dbContext.SaveChangesAsync();
    }

    public async Task SaveAppSettingsAsync(DualViewSettings settings)
    {
        await SaveAsync();
        await updateNotifier.NotifyAppSettingsUpdated();
    }

    public async Task<long> CreateMediaFolder(string folderName, long? parentId)
    {
        var folder = new MediaStorageFolder(folderName.TrimOrThrowIfEmpty(), parentId);

        await dbContext.MediaStorageFolders.AddAsync(folder);
        await SaveAsync();
        await updateNotifier.NotifyMediaFoldersUpdated();
        return folder.Id;
    }

    public async Task AddMediaToFolder(long mediaConfigurationId, string folderPath, bool canCreateRootFolder = false)
    {
        var media = await dbContext.ConfiguredMedia.FirstOrDefaultAsync(m =>
            m.Id == mediaConfigurationId && !m.IsDeleted);

        if (media == null)
            throw new ArgumentException("Media not found");

        // Then parse the folder
        var target = await MediaStorageFolder.GetOrCreateAtPath(folderPath, this, true, canCreateRootFolder);

        if (target == null)
            throw new ArgumentException("Cannot find / create folder at path " + folderPath);

        // Check for duplicate WITHOUT loading the collection
        bool alreadyExists =
            await dbContext.ConfiguredMedia.AnyAsync(m => m.Id == mediaConfigurationId && m.InFolders.Contains(target));

        if (alreadyExists)
        {
            // Do nothing as it is already there
            return;
        }

        target.ContainedItems.Add(media);
        await SaveAsync();
        await updateNotifier.NotifyMediaFolderContentsUpdated(target.Id);
    }

    public async Task RemoveMediaFromFolder(long mediaConfigurationId, string folderPath)
    {
        var media = await dbContext.ConfiguredMedia.FirstOrDefaultAsync(m =>
            m.Id == mediaConfigurationId && !m.IsDeleted);

        if (media == null)
            throw new ArgumentException("Media not found");

        // Resolve folder by path (do not create)
        var target = await GetMediaFolderFromPathAsync(folderPath);
        if (target == null)
            throw new ArgumentException("Cannot find folder at path " + folderPath);

        // Load relation if needed
        var exists = await dbContext.MediaStorageFolders
            .Where(f => f.Id == target.Id)
            .AnyAsync(f => f.ContainedItems.Contains(media));

        if (!exists)
        {
            // Nothing to do
            return;
        }

        // Attach and remove
        await dbContext.Entry(target).Collection(f => f.ContainedItems).LoadAsync();
        target.ContainedItems.Remove(media);
        await SaveAsync();
        await updateNotifier.NotifyMediaFolderContentsUpdated(target.Id);
    }

    public async Task<MediaConfigFolderInfo> GetConfiguredMediaFoldersAsync(long mediaConfigId)
    {
        var media = await dbContext.ConfiguredMedia.FindAsync(mediaConfigId);

        if (media == null)
            throw new ArgumentException("Media not found");

        var folders = await dbContext.MediaStorageFolders.Where(f => f.ContainedItems.Contains(media)).ToListAsync();

        var result = new MediaConfigFolderInfo(media.Name, null!);

        foreach (var folder in folders)
        {
            var fullPath = await GetMediaFolderPath(folder);

            if (string.IsNullOrEmpty(result.PrimaryFolder))
            {
                result.PrimaryFolder = fullPath;
            }
            else
            {
                result.SecondaryFolders ??= new List<string>();
                result.SecondaryFolders.Add(fullPath);
            }
        }

        if (string.IsNullOrEmpty(result.PrimaryFolder))
            throw new Exception("Media is not in any folder");

        return result;
    }

    public async Task<string> GetMediaFolderPath(MediaStorageFolder? folder)
    {
        if (folder == null)
            throw new ArgumentException("Initial folder must be specified");

        var builder = new StringBuilder();

        while (folder != null)
        {
            builder.Insert(0, '/');
            builder.Insert(0, folder.Name);

            var nextId = folder.ParentId;
            if (nextId == null)
                break;

            folder = await dbContext.MediaStorageFolders.FirstOrDefaultAsync(f => f.Id == nextId);
        }

        return builder.ToString();
    }

    public async Task<MediaStorageFolder> CreateMediaFolderAsync(string folderName, long? parentId)
    {
        var folder = new MediaStorageFolder(folderName.TrimOrThrowIfEmpty(), parentId);

        await dbContext.MediaStorageFolders.AddAsync(folder);
        await SaveAsync();
        await updateNotifier.NotifyMediaFoldersUpdated();
        return folder;
    }

    public async Task<bool> SetMediaKeepStatusAsync(long configuredMediaId, bool keep)
    {
        // Load the configured media with the linked media file
        var mediaConfig = await GetConfiguredMediaAsync(configuredMediaId, true);

        if (mediaConfig == null || mediaConfig.IsDeleted || mediaConfig.MediaFile == null)
            throw new ArgumentException("Configured media not found");

        var mediaFile = mediaConfig.MediaFile;

        bool changes = false;

        if (mediaFile.Keep != keep)
        {
            mediaFile.Keep = keep;
            await SaveMediaFileAsync(mediaFile);
            changes = true;
        }

        return changes;
    }

    public async Task<bool> IsMediaSafeToDeleteAsync(long configuredMediaId)
    {
        var media = await dbContext.ConfiguredMedia
            .Include(m => m.InFolders)
            .Include(m => m.MediaFile)
            .ThenInclude(mf => mf.Configurations)
            .AsSplitQuery()
            .FirstOrDefaultAsync(m => m.Id == configuredMediaId);

        if (media == null)
            return false;

        if (IsConfiguredMediaInUse(media))
            return false;

        return true;
    }

    public async Task DeleteMediaAsync(long configuredMediaId)
    {
        var media = await dbContext.ConfiguredMedia.FirstOrDefaultAsync(m => m.Id == configuredMediaId);
        if (media == null || media.IsDeleted)
            return;

        media.IsDeleted = true;
        media.BumpUpdatedAtTime();
        await SaveAsync();
    }

    public async Task RestoreMediaAsync(long configuredMediaId)
    {
        var config = await dbContext.ConfiguredMedia
                         .Include(m => m.MediaFile)
                         .FirstOrDefaultAsync(m => m.Id == configuredMediaId) ??
                     throw new ArgumentException("Media configuration not found");

        if (!config.IsDeleted)
            return;

        logger.LogInformation("Restoring media configuration {Id}", configuredMediaId);
        config.IsDeleted = false;
        config.BumpUpdatedAtTime();

        if (config.MediaFile.IsDeleted)
        {
            logger.LogInformation("Also restoring media file {Id} because its configuration was restored",
                config.MediaFileId);
            config.MediaFile.IsDeleted = false;
        }

        await SaveMediaConfigAsync(config);
        await updateNotifier.NotifyMediaUpdated(config.Id);
    }

    public async Task<List<ConfiguredMedia>> GetDeletedMediaAsync(int limit)
    {
        return await dbContext.ConfiguredMedia
            .Include(m => m.MediaFile)
            .Where(m => m.IsDeleted)
            .OrderByDescending(m => m.UpdatedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<ConfiguredMediaDTO>> GetConfiguredMediaSiblingsAsync(long mediaConfigId)
    {
        var media = await dbContext.ConfiguredMedia.FindAsync(mediaConfigId);

        if (media == null)
            return new List<ConfiguredMediaDTO>();

        var allConfigs = await dbContext.ConfiguredMedia.Where(c => c.MediaFileId == media.MediaFileId).ToListAsync();

        return allConfigs.Where(c => c.Id != mediaConfigId).Select(c => c.GetDTO()).ToList();
    }

    public async Task<ConfiguredMediaDTO> CreateConfiguredMediaAsync(long mediaId, string configName,
        List<string> folders)
    {
        var mainFolder = folders.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(mainFolder))
            throw new ArgumentException("Must provide at least one folder");

        var originalMedia = await GetMediaByIdAsync(mediaId);

        if (originalMedia == null)
            throw new ArgumentException("Media not found");

        var newConfig = await CreateMediaConfig(originalMedia, configName, mainFolder, true);

        // Add the extra folders
        for (int i = 1; i < folders.Count; ++i)
        {
            var folder = folders[i];
            try
            {
                await AddMediaToFolder(newConfig.Id, folder, true);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to add media to folder {Folder}, but will continue creation", folder);
                continue;
            }

            logger.LogInformation("Added new media config {Id} to extra folder: {Folder}", newConfig.Id, folder);
        }

        return newConfig.GetDTO();
    }

    public Task SaveConfiguredMediaAsync(ConfiguredMediaDTO media)
    {
        // Due to clearing thumbnails etc. checks that very complex logic is not duplicated here from
        // MediaController.UpdateConfig
        throw new NotSupportedException(
            "This edit is so complex that it has to go through the MediaController (instead of directly the database)");
    }

    public async Task<ConfiguredMediaDTO?> GetPrimeConfiguredMediaFromFileAsync(long mediaFileId)
    {
        return (await GetConfiguredMediaByMediaFileAsync(mediaFileId, true))?.GetDTO();
    }

    public async Task<List<MediaStorageFolder>> GetMediaFoldersAsync(long? limitToParent)
    {
        if (limitToParent != null)
        {
            return await dbContext.MediaStorageFolders.Where(f => f.ParentId == limitToParent).ToListAsync();
        }

        return await dbContext.MediaStorageFolders.ToListAsync();
    }

    public async Task<MediaStorageFolder?> GetMediaFolderAsync(long id)
    {
        return await dbContext.MediaStorageFolders.FindAsync(id);
    }

    public async Task<MediaStorageFolder?> GetMediaFolderAsync(string name, long? parentFolderId)
    {
        return await dbContext.MediaStorageFolders.FirstOrDefaultAsync(f =>
            f.Name == name && f.ParentId == parentFolderId);
    }

    public async Task<MediaStorageFolder?> GetMediaFolderFromPathAsync(string path)
    {
        return await MediaStorageFolder.GetOrCreateAtPath(path.TrimEnd(), this, false);
    }

    public async Task<List<ConfiguredMedia>> GetMediaInFolderAsync(long folderId)
    {
        var folder = await dbContext.MediaStorageFolders.FindAsync(folderId) ??
                     throw new ArgumentException("Folder not found");
        return await dbContext.ConfiguredMedia.Where(m => m.InFolders.Contains(folder) && !m.IsDeleted).ToListAsync();
    }

    public async Task<ConfiguredMedia?> GetMediaByNameAndPath(string requestName, string mainFolder)
    {
        var folder = await MediaStorageFolder.GetOrCreateAtPath(mainFolder, this, false);

        // Can't conflict if the path doesn't exist
        if (folder == null)
            return null;

        return await dbContext.ConfiguredMedia.FirstOrDefaultAsync(m =>
            m.Name == requestName && m.InFolders.Contains(folder));
    }

    public async Task<Tuple<List<ConfiguredMediaInfo>, int>> GetMediaFolderContents(long folderId, int itemPage,
        int pageSize, FolderSortColumn sortColumn, SortDirection sortDirection)
    {
        var query = dbContext.ConfiguredMedia.Where(m => m.InFolders.Any(f => f.Id == folderId) && !m.IsDeleted);

        var total = await query.CountAsync();

        var totalPages = (int)Math.Ceiling(total / (double)pageSize);

        if (sortDirection == SortDirection.Ascending)
        {
            switch (sortColumn)
            {
                case FolderSortColumn.Name:
                    query = query.OrderBy(c => c.Name).ThenBy(c => c.Id);
                    break;
                case FolderSortColumn.DateCreated:
                    query = query.OrderBy(c => c.CreatedAt);
                    break;
                case FolderSortColumn.DateModified:
                    query = query.OrderBy(c => c.UpdatedAt);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(sortColumn), sortColumn, null);
            }
        }
        else
        {
            switch (sortColumn)
            {
                case FolderSortColumn.Name:
                    query = query.OrderByDescending(c => c.Name).ThenByDescending(c => c.Id);
                    break;
                case FolderSortColumn.DateCreated:
                    query = query.OrderByDescending(c => c.CreatedAt);
                    break;
                case FolderSortColumn.DateModified:
                    query = query.OrderByDescending(c => c.UpdatedAt);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(sortColumn), sortColumn, null);
            }
        }

        query = query.Skip(itemPage * pageSize).Take(pageSize);

        return Tuple.Create(
            await query.Select(c =>
                    new ConfiguredMediaInfo(c.Name, c.Id, c.Prime, c.MediaFileId, c.MediaType, c.Width, c.Height,
                        c.MaskEnabled))
                .ToListAsync(),
            totalPages);
    }

    public async Task<MediaFile?> GetMediaByHashAsync(string sha3)
    {
        return await dbContext.MediaFiles.FirstOrDefaultAsync(m => m.HashSha3 == sha3);
    }

    public async Task<MediaFile?> GetMediaByIdAsync(long id)
    {
        return await dbContext.MediaFiles.FindAsync(id);
    }

    public async Task SaveMediaFileAsync(MediaFile mediaFile)
    {
        await SaveAsync();
        await updateNotifier.NotifyMediaUpdated(mediaFile.Id);
    }

    public Task<List<ConfiguredMedia>> GetMediaConfigurationsAsync(long mediaFileId)
    {
        return dbContext.ConfiguredMedia.Where(c => c.MediaFileId == mediaFileId && !c.IsDeleted).ToListAsync();
    }

    public async Task<ConfiguredMedia> GetConfiguredMediaPrimeAsync(long mediaId)
    {
        return await dbContext.ConfiguredMedia.FirstOrDefaultAsync(c => c.Prime && c.MediaFileId == mediaId) ??
               throw new InvalidOperationException("Media file has no prime configuration");
    }

    public async Task<ConfiguredMedia?> GetConfiguredMediaAsync(long id, bool loadMedia = false)
    {
        if (loadMedia)
        {
            return await dbContext.ConfiguredMedia.Include(c => c.MediaFile).FirstOrDefaultAsync(c => c.Id == id);
        }

        return await dbContext.ConfiguredMedia.FindAsync(id);
    }

    public Task<ConfiguredMedia?> GetConfiguredMediaByMediaFileAsync(long id, bool loadMedia = false)
    {
        var query = dbContext.ConfiguredMedia.Where(c => c.MediaFileId == id && c.Prime && !c.IsDeleted);

        if (loadMedia)
        {
            query = query.Include(c => c.MediaFile);
        }

        return query.FirstOrDefaultAsync();
    }

    public async Task<ConfiguredMedia> CreateMediaConfig(MediaFile originalMedia, string newName, string mainFolder,
        bool markAsKeep, bool allowCreateFolder = true, bool allowRootFolderCreate = false)
    {
        if (originalMedia.IsDeleted)
            throw new InvalidOperationException("Cannot create a new configuration for a deleted media file");

        if (!originalMedia.Keep && markAsKeep)
        {
            originalMedia.Keep = true;
        }

        var newConfig = new ConfiguredMedia(newName)
        {
            MediaFileId = originalMedia.Id,
            MediaFile = originalMedia,
            Prime = false,
            Width = originalMedia.Width,
            Height = originalMedia.Height,
            FrameCount = originalMedia.FrameCount,
            FramesPerSecond = originalMedia.FramesPerSecond,
            MediaType = originalMedia.MediaType,
        };

        var folder =
            await MediaStorageFolder.GetOrCreateAtPath(mainFolder, this, allowCreateFolder, allowRootFolderCreate);

        if (folder == null)
            throw new ArgumentException("Invalid folder to put new media config in");

        newConfig.InFolders.Add(folder);

        await dbContext.ConfiguredMedia.AddAsync(newConfig);
        await dbContext.SaveChangesAsync();

        await updateNotifier.NotifyMediaUpdated(originalMedia.Id);
        await updateNotifier.NotifyMediaFolderContentsUpdated(folder.Id);

        return newConfig;
    }

    public async Task MakeSureMediaIsSetToKeep(long configuredMediaId)
    {
        var media = await GetConfiguredMediaAsync(configuredMediaId, true);

        if (media == null || media.MediaFile == null)
            throw new ArgumentException("Media does not exist");

        // Restore and mark as keep to keep as thumbnail
        if (media.IsDeleted)
        {
            media.IsDeleted = false;
            media.BumpUpdatedAtTime();

            if (!media.MediaFile.Keep)
            {
                await SaveMediaFileAsync(media.MediaFile);
            }
        }
        else if (!media.MediaFile.Keep || media.MediaFile.IsDeleted)
        {
            media.MediaFile.IsDeleted = false;
            media.MediaFile.Keep = true;
            await SaveMediaFileAsync(media.MediaFile);
        }
    }

    public async Task<ConfiguredMedia> CreateMediaAsync(MediaFile mediaItem, string initialFolder,
        bool canCreateRootFolder = false)
    {
        var folder =
            await MediaStorageFolder.GetOrCreateAtPath(initialFolder.TrimEnd(), this, true, canCreateRootFolder);

        if (folder == null)
            throw new ArgumentException("Cannot find / create folder at path " + initialFolder);

        var primeConfig = new ConfiguredMedia(ConfiguredMedia.AdjustedNameFromOriginal(mediaItem.OriginalFileName))
        {
            Prime = true,
            MediaFile = mediaItem,
            InFolders = new List<MediaStorageFolder>
            {
                folder,
            },
            Width = mediaItem.Width,
            Height = mediaItem.Height,
            FrameCount = mediaItem.FrameCount,
            FramesPerSecond = mediaItem.FramesPerSecond,
            MediaType = mediaItem.MediaType,
        };

        await dbContext.MediaFiles.AddAsync(mediaItem);

        mediaItem.Configurations.Add(primeConfig);
        await dbContext.ConfiguredMedia.AddAsync(primeConfig);

        await SaveAsync();

        await updateNotifier.NotifyMediaFolderContentsUpdated(folder.Id);
        return primeConfig;
    }

    public async Task SaveMediaConfigAsync(ConfiguredMedia media)
    {
        await SaveAsync();
        await updateNotifier.NotifyMediaUpdated(media.Id);
    }

    public async Task<List<ConfiguredMedia>> GetOldDeletedConfigsAsync(DateTime cutoff)
    {
        return await dbContext.ConfiguredMedia
            .Include(m => m.MediaFile)
            .Where(m => m.IsDeleted && m.UpdatedAt < cutoff)
            .ToListAsync();
    }

    public async Task PurgeConfiguredMediaAsync(ConfiguredMedia config)
    {
        dbContext.ConfiguredMedia.Remove(config);
        await SaveAsync();
    }

    public async Task<List<MediaFile>> GetEligibleMediaFilesForPurgeAsync()
    {
        // We want to delete MediaFiles that are not marked Keep and have no configurations
        return await dbContext.MediaFiles
            .Include(mf => mf.Configurations)
            .Where(mf => !mf.Keep && !mf.Configurations.Any())
            .ToListAsync();
    }

    public async Task PurgeMediaFileAsync(MediaFile mediaFile)
    {
        dbContext.MediaFiles.Remove(mediaFile);
        await SaveAsync();
    }

    public async Task<MaintenanceJobRecord?> GetMaintenanceRecord(string name)
    {
        return await dbContext.MaintenanceJobRecords.FindAsync(name);
    }

    public async Task<MaintenanceJobRecord?> GetOldestMaintenanceJobToRun(DateTime afterTime)
    {
        return await dbContext.MaintenanceJobRecords
            .Where(j => j.NextRunAfter <= afterTime)
            .OrderBy(j => j.NextRunAfter)
            .FirstOrDefaultAsync();
    }

    public async Task CreateMaintenanceRecord(MaintenanceJobRecord record)
    {
        await dbContext.MaintenanceJobRecords.AddAsync(record);
        await SaveAsync();
    }

    public Task SaveMaintenanceRecord(MaintenanceJobRecord record)
    {
        return SaveAsync();
    }

    public Task DeleteMaintenanceRecord(MaintenanceJobRecord record)
    {
        dbContext.MaintenanceJobRecords.Remove(record);
        return SaveAsync();
    }

    public Task BeginTransaction()
    {
        return dbContext.Database.BeginTransactionAsync();
    }

    public Task CommitTransaction()
    {
        return dbContext.Database.CommitTransactionAsync();
    }

    public Task RollbackTransaction()
    {
        return dbContext.Database.RollbackTransactionAsync();
    }

    public Task ReloadEntity(MediaFile mediaFile)
    {
        return dbContext.Entry(mediaFile).ReloadAsync();
    }

    // Server-side prerendering compatibility with IClientDatabaseService
    async Task<List<MediaStorageFolderInfo>> IClientDatabaseService.GetMediaFoldersAsync(long? limitToParent)
    {
        return (await GetMediaFoldersAsync(limitToParent)).ConvertToInfo<MediaStorageFolder, MediaStorageFolderInfo>();
    }

    async Task<MediaStorageFolderDTO?> IClientDatabaseService.GetMediaFolderAsync(long id)
    {
        return (await GetMediaFolderAsync(id))?.GetDTO();
    }

    async Task<MediaStorageFolderDTO?> IClientDatabaseService.GetMediaFolderFromPathAsync(string path)
    {
        return (await GetMediaFolderFromPathAsync(path))?.GetDTO();
    }

    async Task<List<ConfiguredMediaDTO>> IClientDatabaseService.GetDeletedMediaAsync(int limit)
    {
        return (await GetDeletedMediaAsync(limit)).ConvertToDTO<ConfiguredMedia, ConfiguredMediaDTO>();
    }

    async Task<ConfiguredMediaDTO?> IClientDatabaseService.GetConfiguredMediaAsync(long mediaConfigId)
    {
        return (await GetConfiguredMediaAsync(mediaConfigId, true))?.GetDTO();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposed)
        {
            if (disposing)
            {
                // Dispose of managed resources
                dbContext.Dispose();
            }

            disposed = true;
        }
    }

    private bool IsConfiguredMediaInUse(ConfiguredMedia media)
    {
        if (media.MediaFile.Configurations.Count >= 2)
            return true;

        return media.InFolders.Any();
    }
}
