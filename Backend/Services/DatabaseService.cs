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
        var folder = new MediaFolder(folderName.TrimOrThrowIfEmpty(), parentId);

        await dbContext.MediaFolders.AddAsync(folder);
        await SaveAsync();
        await updateNotifier.NotifyMediaFoldersUpdated();
        return folder.Id;
    }

    public async Task<long> CreateCollection(string collectionName, long folderId)
    {
        var collection = new Collection(collectionName.TrimOrThrowIfEmpty(), folderId);

        await dbContext.Collections.AddAsync(collection);
        await SaveAsync();
        // TODO: notify
        return collection.Id;
    }

    public async Task AddMediaToCollection(long mediaId, long collectionId, int sequenceNumber)
    {
        var alreadyExists = await dbContext.Set<CollectionItem>().AnyAsync(ci =>
            ci.CollectionId == collectionId && ci.MediaFileId == mediaId);

        if (alreadyExists)
            return;

        var item = new CollectionItem
        {
            CollectionId = collectionId,
            MediaFileId = mediaId,
            SequenceNumber = sequenceNumber
        };

        await dbContext.Set<CollectionItem>().AddAsync(item);
        await SaveAsync();
    }

    public async Task RemoveMediaFromCollection(long mediaId, long collectionId)
    {
        var item = await dbContext.Set<CollectionItem>().FirstOrDefaultAsync(ci =>
            ci.CollectionId == collectionId && ci.MediaFileId == mediaId);

        if (item == null)
            return;

        dbContext.Set<CollectionItem>().Remove(item);
        await SaveAsync();
    }

    public async Task<List<long>> GetMediaCollectionsAsync(long mediaId)
    {
        return await dbContext.Set<CollectionItem>()
            .Where(ci => ci.MediaFileId == mediaId)
            .Select(ci => ci.CollectionId)
            .ToListAsync();
    }

    public async Task<string> GetMediaFolderPath(MediaFolder? folder)
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

            folder = await dbContext.MediaFolders.FirstOrDefaultAsync(f => f.Id == nextId);
        }

        return builder.ToString();
    }

    public async Task<MediaFolder> CreateMediaFolderAsync(string folderName, long? parentId)
    {
        var folder = new MediaFolder(folderName.TrimOrThrowIfEmpty(), parentId);

        await dbContext.MediaFolders.AddAsync(folder);
        await SaveAsync();
        await updateNotifier.NotifyMediaFoldersUpdated();
        return folder;
    }

    public async Task<bool> SetMediaKeepStatusAsync(long mediaId, bool keep)
    {
        var mediaFile = await GetMediaByIdAsync(mediaId);

        if (mediaFile == null || mediaFile.IsDeleted)
            throw new ArgumentException("Media not found");

        bool changes = false;

        if (mediaFile.Keep != keep)
        {
            mediaFile.Keep = keep;
            await SaveMediaFileAsync(mediaFile);
            changes = true;
        }

        return changes;
    }

    public async Task<bool> IsMediaSafeToDeleteAsync(long mediaId)
    {
        var media = await dbContext.MediaFiles
            .Include(m => m.InCollections)
            .FirstOrDefaultAsync(m => m.Id == mediaId);

        if (media == null)
            return false;

        // For now, if it's in any collection, it's not safe to delete? 
        // Or if it's marked as keep.
        if (media.Keep)
            return false;

        return true;
    }

    public async Task DeleteMediaAsync(long mediaId)
    {
        var media = await dbContext.MediaFiles.FirstOrDefaultAsync(m => m.Id == mediaId);
        if (media == null || media.IsDeleted)
            return;

        media.IsDeleted = true;
        await SaveAsync();
    }

    public async Task RestoreMediaAsync(long mediaId)
    {
        var media = await dbContext.MediaFiles
                         .FirstOrDefaultAsync(m => m.Id == mediaId) ??
                     throw new ArgumentException("Media file not found");

        if (!media.IsDeleted)
            return;

        logger.LogInformation("Restoring media file {Id}", mediaId);
        media.IsDeleted = false;
        await SaveAsync();
    }

    public async Task<List<MediaFile>> GetDeletedMediaAsync(int limit)
    {
        return await dbContext.MediaFiles
            .Where(m => m.IsDeleted)
            .OrderByDescending(m => m.ImportedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<MediaFile>> GetMediaFileSiblingsAsync(long mediaId)
    {
        var collectionIds = await GetMediaCollectionsAsync(mediaId);

        return await dbContext.Set<CollectionItem>()
            .Where(ci => collectionIds.Contains(ci.CollectionId) && ci.MediaFileId != mediaId)
            .Select(ci => ci.MediaFile)
            .Distinct()
            .ToListAsync();
    }

    async Task<List<MediaFileDTO>> IClientDatabaseService.GetMediaFileSiblingsAsync(long mediaId)
    {
        return (await GetMediaFileSiblingsAsync(mediaId)).Select(m => m.GetDTO()).ToList();
    }

    public async Task<MediaFileDTO> CreateMediaFileAsync(MediaFileDTO mediaFile, long collectionId)
    {
        var newMedia = new MediaFile(mediaFile.OriginalFileName, mediaFile.HashSha3)
        {
            MediaType = mediaFile.MediaType,
            Width = mediaFile.Width,
            Height = mediaFile.Height,
            FrameCount = mediaFile.FrameCount,
            FramesPerSecond = mediaFile.FramesPerSecond,
            Keep = mediaFile.Keep,
            ParentMediaId = mediaFile.ParentMediaId
        };

        var result = await CreateMediaAsync(newMedia, collectionId);
        return result.GetDTO();
    }

    public async Task SaveMediaFileAsync(MediaFileDTO media)
    {
        var existing = await dbContext.MediaFiles.FindAsync(media.Id) ??
                       throw new ArgumentException("Media not found");

        existing.Keep = media.Keep;
        existing.IsDeleted = media.IsDeleted;
        existing.CropLeft = media.CropLeft;
        existing.CropTop = media.CropTop;
        existing.CropRight = media.CropRight;
        existing.CropBottom = media.CropBottom;

        await SaveAsync();
    }

    public async Task<List<MediaFolder>> GetMediaFoldersAsync(long? limitToParent)
    {
        if (limitToParent != null)
        {
            return await dbContext.MediaFolders.Where(f => f.ParentId == limitToParent).ToListAsync();
        }

        return await dbContext.MediaFolders.ToListAsync();
    }

    public async Task<MediaFolder?> GetMediaFolderAsync(long id)
    {
        return await dbContext.MediaFolders.FindAsync(id);
    }

    public async Task<MediaFolder?> GetMediaFolderAsync(string name, long? parentFolderId)
    {
        return await dbContext.MediaFolders.FirstOrDefaultAsync(f =>
            f.Name == name && f.ParentId == parentFolderId);
    }

    public async Task<MediaFolder?> GetMediaFolderFromPathAsync(string path)
    {
        return await MediaFolder.GetOrCreateAtPath(path.TrimEnd(), this, false);
    }

    public async Task<Tuple<List<CollectionDTO>, int>> GetFolderCollections(long folderId, int page, int pageSize)
    {
        var query = dbContext.Collections.Where(c => c.FolderId == folderId);
        var total = await query.CountAsync();
        var items = await query.OrderBy(c => c.Name)
            .Skip(page * pageSize)
            .Take(pageSize)
            .Select(c => c.GetDTO())
            .ToListAsync();

        return new Tuple<List<CollectionDTO>, int>(items, total);
    }

    public async Task<Tuple<List<MediaFileDTO>, int>> GetCollectionContents(long collectionId, int page, int pageSize,
        FolderSortColumn sortColumn, SortDirection sortDirection)
    {
        var query = dbContext.Set<CollectionItem>()
            .Where(ci => ci.CollectionId == collectionId);

        var total = await query.CountAsync();

        // TODO: sorting
        var items = await query.OrderBy(ci => ci.SequenceNumber)
            .Skip(page * pageSize)
            .Take(pageSize)
            .Select(ci => ci.MediaFile.GetDTO())
            .ToListAsync();

        return new Tuple<List<MediaFileDTO>, int>(items, total);
    }
    public async Task<List<Collection>> GetCollectionsInFolderAsync(long folderId)
    {
        return await dbContext.Collections.Where(c => c.FolderId == folderId).ToListAsync();
    }

    public async Task<Collection?> GetCollectionByNameAndFolder(string name, long folderId)
    {
        return await dbContext.Collections.FirstOrDefaultAsync(c => c.Name == name && c.FolderId == folderId);
    }

    public async Task<Collection?> GetCollectionAsync(long id)
    {
        return await dbContext.Collections.FindAsync(id);
    }

    public async Task SaveCollectionAsync(Collection collection)
    {
        await SaveAsync();
    }

    public async Task DeleteCollectionAsync(Collection collection)
    {
        dbContext.Collections.Remove(collection);
        await SaveAsync();
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

    public async Task MakeSureMediaIsSetToKeep(long mediaId)
    {
        var media = await dbContext.MediaFiles.FindAsync(mediaId) ?? throw new ArgumentException("Media not found");
        media.Keep = true;
        await SaveAsync();
    }

    public async Task<MediaFile> CreateMediaAsync(MediaFile mediaItem, long collectionId)
    {
        await dbContext.MediaFiles.AddAsync(mediaItem);
        await SaveAsync();

        // Get the next sequence number
        var sequenceNumber = await dbContext.Set<CollectionItem>()
            .Where(ci => ci.CollectionId == collectionId)
            .Select(ci => ci.SequenceNumber)
            .DefaultIfEmpty(0)
            .MaxAsync() + 1;

        await AddMediaToCollection(mediaItem.Id, collectionId, sequenceNumber);
        
        return mediaItem;
    }

    public async Task<List<MediaFile>> GetEligibleMediaFilesForPurgeAsync()
    {
        return await dbContext.MediaFiles
            .Where(m => m.IsDeleted && !m.Keep)
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

    // Note: these are dummy compatibility methods that can be deleted when not found useful when converting
    async Task<ConfiguredMediaDTO?> IClientDatabaseService.GetConfiguredMediaAsync(long mediaConfigId)
    {
        var media = await GetMediaByIdAsync(mediaConfigId);
        return media != null ? new ConfiguredMediaDTO(media.GetDTO()) : null;
    }

    async Task<MediaConfigFolderInfo> IClientDatabaseService.GetConfiguredMediaFoldersAsync(long mediaConfigId)
    {
        return new MediaConfigFolderInfo("Media", "Root");
    }

    async Task<List<ConfiguredMediaDTO>> IClientDatabaseService.GetConfiguredMediaSiblingsAsync(long mediaConfigId)
    {
        var siblings = await GetMediaFileSiblingsAsync(mediaConfigId);
        return siblings.Select(s => new ConfiguredMediaDTO(s.GetDTO())).ToList();
    }

    async Task<Tuple<List<ConfiguredMediaInfo>, int>> IClientDatabaseService.GetMediaFolderContents(long folderId,
        int itemPage, int pageSize, FolderSortColumn sortColumn, SortDirection sortDirection)
    {
        return new Tuple<List<ConfiguredMediaInfo>, int>(new List<ConfiguredMediaInfo>(), 0);
    }

    Task IClientDatabaseService.AddMediaToFolder(long mediaConfigurationId, string folderPath, bool canCreateRootFolder)
    {
        return Task.CompletedTask;
    }

    Task IClientDatabaseService.RemoveMediaFromFolder(long mediaConfigurationId, string folderPath)
    {
        return Task.CompletedTask;
    }

    Task<ConfiguredMediaDTO> IClientDatabaseService.CreateConfiguredMediaAsync(long mediaId, string configName,
        List<string> folders)
    {
        throw new NotSupportedException();
    }

    Task IClientDatabaseService.SaveConfiguredMediaAsync(ConfiguredMediaDTO media)
    {
        return ((IClientDatabaseService)this).SaveMediaFileAsync(media);
    }

    async Task<ConfiguredMediaDTO?> IClientDatabaseService.GetPrimeConfiguredMediaFromFileAsync(long mediaFileId)
    {
        var media = await GetMediaByIdAsync(mediaFileId);
        return media != null ? new ConfiguredMediaDTO(media.GetDTO()) : null;
    }

    // Server-side prerendering compatibility with IClientDatabaseService
    async Task<List<MediaFolderInfo>> IClientDatabaseService.GetMediaFoldersAsync(long? limitToParent)
    {
        return (await GetMediaFoldersAsync(limitToParent)).Select(f => f.GetInfo()).ToList();
    }

    async Task<MediaFolderDTO?> IClientDatabaseService.GetMediaFolderAsync(long id)
    {
        return (await GetMediaFolderAsync(id))?.GetDTO();
    }

    async Task<MediaFolderDTO?> IClientDatabaseService.GetMediaFolderFromPathAsync(string path)
    {
        return (await GetMediaFolderFromPathAsync(path))?.GetDTO();
    }

    async Task<MediaFileDTO?> IClientDatabaseService.GetMediaFileAsync(long mediaId)
    {
        return (await GetMediaByIdAsync(mediaId))?.GetDTO();
    }

    async Task<List<MediaFileDTO>> IClientDatabaseService.GetDeletedMediaAsync(int limit)
    {
        return (await GetDeletedMediaAsync(limit)).Select(m => m.GetDTO()).ToList();
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
}
