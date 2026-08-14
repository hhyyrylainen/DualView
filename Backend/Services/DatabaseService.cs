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
    private readonly IDataFolderService dataFolderService;
    private readonly IMediaProcessingService mediaProcessingService;

    private readonly TimeSpan oldChatThreshold = TimeSpan.FromHours(24);

    private bool disposed;

    public DatabaseService(ILogger<DatabaseService> logger, AppDbContext dbContext,
        IEntityUpdateNotifier updateNotifier, IAppEvents appEvents, IDataFolderService dataFolderService,
        IMediaProcessingService mediaProcessingService)
    {
        this.logger = logger;
        this.dbContext = dbContext;
        this.updateNotifier = updateNotifier;
        this.appEvents = appEvents;
        this.dataFolderService = dataFolderService;
        this.mediaProcessingService = mediaProcessingService;
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

    public async Task<long> CreateMediaFolder(string folderName, long parentId)
    {
        var trimmedName = folderName.TrimOrThrowIfEmpty();
        if (trimmedName.Length > 200)
            throw new ArgumentException("Folder name is too long");

        var folder = new MediaFolder(trimmedName);

        var parent = await dbContext.MediaFolders.FindAsync(parentId);
        if (parent == null)
            throw new Exception("Parent folder ID not found");

        if (await dbContext.MediaFolders.AnyAsync(existing =>
                existing.NameLowerCase == folder.NameLowerCase &&
                existing.Parents.Any(parentFolder => parentFolder.Id == parentId)))
        {
            throw new InvalidOperationException("A folder with that name already exists here");
        }

        folder.Parents.Add(parent);

        await dbContext.MediaFolders.AddAsync(folder);
        await SaveAsync();
        await updateNotifier.NotifyMediaFoldersUpdated();
        return folder.Id;
    }

    public async Task RenameMediaFolder(long folderId, string folderName)
    {
        var trimmedName = folderName.TrimOrThrowIfEmpty();
        if (trimmedName.Length > 200)
            throw new ArgumentException("Folder name is too long");

        var folder = await dbContext.MediaFolders.Include(item => item.Parents)
            .FirstOrDefaultAsync(item => item.Id == folderId);
        if (folder == null)
            throw new Exception("Folder not found");

        var lowerName = trimmedName.ToLowerInvariant();
        if (folder.Parents.Any(parent => dbContext.MediaFolders.Any(other =>
                other.Id != folderId && other.NameLowerCase == lowerName &&
                other.Parents.Any(parentFolder => parentFolder.Id == parent.Id))))
        {
            throw new Exception("A folder with that name already exists here");
        }

        folder.Name = trimmedName;
        await SaveAsync();
        await updateNotifier.NotifyMediaFoldersUpdated();
    }

    public async Task<long> CreateCollection(string collectionName, long folderId)
    {
        var trimmedName = collectionName.TrimOrThrowIfEmpty();
        if (trimmedName.Length > 200)
            throw new ArgumentException("Collection name is too long");

        var collection = new Collection(trimmedName);

        if (await dbContext.Collections.AnyAsync(existing => existing.NameLowerCase == collection.NameLowerCase))
            throw new InvalidOperationException("A collection with that name already exists");

        var folder = await dbContext.MediaFolders.FindAsync(folderId);
        if (folder != null)
        {
            collection.Folders.Add(folder);
        }
        else
        {
            throw new Exception("Folder ID not found");
        }

        await dbContext.Collections.AddAsync(collection);
        await SaveAsync();
        await updateNotifier.NotifyMediaFolderContentsUpdated(folderId);
        return collection.Id;
    }

    public async Task RenameCollection(long collectionId, string collectionName)
    {
        var trimmedName = collectionName.TrimOrThrowIfEmpty();
        if (trimmedName.Length > 200)
            throw new ArgumentException("Collection name is too long");
        if (trimmedName.Contains('/'))
            throw new ArgumentException("Collection name cannot contain '/'");

        var collection = await dbContext.Collections.FirstOrDefaultAsync(item => item.Id == collectionId);
        if (collection == null)
            throw new Exception("Collection not found");

        var lowerName = trimmedName.ToLowerInvariant();
        if (await dbContext.Collections.AnyAsync(existing =>
                existing.Id != collectionId && existing.NameLowerCase == lowerName))
        {
            throw new InvalidOperationException("A collection with that name already exists");
        }

        collection.Name = trimmedName;
        await SaveAsync();
        await updateNotifier.NotifyCollectionUpdated(collectionId);
    }

    public async Task AddCollectionToFolder(long collectionId, long folderId)
    {
        var collection = await dbContext.Collections.Include(c => c.Folders)
            .FirstOrDefaultAsync(c => c.Id == collectionId);
        if (collection == null)
            throw new Exception("Collection not found");

        var folder = await dbContext.MediaFolders.FindAsync(folderId);
        if (folder == null)
            throw new Exception("Folder not found");

        var rootFolder = folderId == MediaFolder.RootFolderId
            ? null
            : collection.Folders.FirstOrDefault(f => f.Id == MediaFolder.RootFolderId);
        var removedFromRoot = rootFolder != null;
        if (rootFolder != null)
            collection.Folders.Remove(rootFolder);

        if (collection.Folders.Any(f => f.Id == folderId))
        {
            if (removedFromRoot)
            {
                await SaveAsync();
                await updateNotifier.NotifyMediaFolderContentsUpdated(MediaFolder.RootFolderId);
            }

            return;
        }

        collection.Folders.Add(folder);
        await SaveAsync();
        await updateNotifier.NotifyMediaFolderContentsUpdated(folderId);
        if (removedFromRoot)
            await updateNotifier.NotifyMediaFolderContentsUpdated(MediaFolder.RootFolderId);
    }

    public async Task AddFolderToFolder(long folderId, long parentFolderId)
    {
        if (folderId == parentFolderId)
            throw new Exception("Cannot add a folder to itself");

        var folder = await dbContext.MediaFolders.Include(f => f.Parents)
            .FirstOrDefaultAsync(f => f.Id == folderId);
        if (folder == null)
            throw new Exception("Folder not found");

        var parent = await dbContext.MediaFolders.FindAsync(parentFolderId);
        if (parent == null)
            throw new Exception("Parent folder not found");

        if (folder.Parents.Any(p => p.Id == parentFolderId))
            return;

        folder.Parents.Add(parent);
        await SaveAsync();
        await updateNotifier.NotifyMediaFoldersUpdated();
    }

    public async Task RemoveCollectionFromFolder(long collectionId, long folderId)
    {
        var collection = await dbContext.Collections.Include(c => c.Folders)
            .FirstOrDefaultAsync(c => c.Id == collectionId);
        if (collection == null)
            throw new Exception("Collection not found");

        var folder = collection.Folders.FirstOrDefault(f => f.Id == folderId);
        if (folder == null)
            return;

        collection.Folders.Remove(folder);

        bool addedToRoot = false;
        if (collection.Folders.Count == 0)
        {
            var root = await dbContext.MediaFolders.FindAsync(MediaFolder.RootFolderId);
            if (root != null)
            {
                collection.Folders.Add(root);
                addedToRoot = true;
            }
        }

        await SaveAsync();
        await updateNotifier.NotifyMediaFolderContentsUpdated(folderId);

        if (addedToRoot && folderId != MediaFolder.RootFolderId)
            await updateNotifier.NotifyMediaFolderContentsUpdated(MediaFolder.RootFolderId);
    }

    public async Task RemoveFolderFromFolder(long folderId, long parentFolderId)
    {
        var folder = await dbContext.MediaFolders.Include(f => f.Parents)
            .FirstOrDefaultAsync(f => f.Id == folderId);
        if (folder == null)
            throw new Exception("Folder not found");

        var parent = folder.Parents.FirstOrDefault(p => p.Id == parentFolderId);
        if (parent == null)
            return;

        folder.Parents.Remove(parent);

        if (folder.Parents.Count == 0 && folder.Id != MediaFolder.RootFolderId)
        {
            var root = await dbContext.MediaFolders.FindAsync(MediaFolder.RootFolderId);
            if (root != null)
                folder.Parents.Add(root);
        }

        await SaveAsync();
        await updateNotifier.NotifyMediaFoldersUpdated();
    }

    public async Task AddMediaToCollection(List<long> mediaIds, long collectionId, int firstSequenceNumber,
        List<int>? sequenceNumbers = null)
    {
        if (sequenceNumbers != null && sequenceNumbers.Count != mediaIds.Count)
            throw new ArgumentException("The sequence number list must match the media ID list length.");

        var collection = await dbContext.Collections.FindAsync(collectionId) ??
                         throw new ArgumentException("Collection not found");

        if (mediaIds.Count == 0)
            return;

        var existingMediaIds = await dbContext.Set<CollectionItem>()
            .Where(ci => ci.CollectionId == collectionId && mediaIds.Contains(ci.MediaFileId))
            .Select(ci => ci.MediaFileId)
            .ToHashSetAsync();
        var newMediaIds = new List<long>();
        var sequenceNumbersByMediaId = new Dictionary<long, int>();
        for (var index = 0; index < mediaIds.Count; ++index)
        {
            var mediaId = mediaIds[index];
            if (existingMediaIds.Contains(mediaId) || !sequenceNumbersByMediaId.TryAdd(mediaId,
                    sequenceNumbers == null ? firstSequenceNumber + index : sequenceNumbers[index]))
            {
                continue;
            }

            newMediaIds.Add(mediaId);
        }

        if (newMediaIds.Count == 0)
            return;

        var mediaFiles = await dbContext.MediaFiles
            .Where(media => newMediaIds.Contains(media.Id))
            .ToDictionaryAsync(media => media.Id);
        if (mediaFiles.Count != newMediaIds.Count)
            throw new ArgumentException("One or more media files were not found.");

        var activeItemCount = await dbContext.Set<CollectionItem>()
            .Where(ci => ci.CollectionId == collectionId && !ci.MediaFile.IsDeleted)
            .CountAsync();
        var activeNewItemCount = newMediaIds.Count(mediaId => !mediaFiles[mediaId].IsDeleted);
        if ((activeItemCount + activeNewItemCount) % collection.ImageGroupSize != 0)
        {
            throw new InvalidOperationException(
                $"The collection requires images to be added in groups of {collection.ImageGroupSize}.");
        }

        var items = newMediaIds.Select(mediaId => new CollectionItem
        {
            CollectionId = collectionId,
            MediaFileId = mediaId,
            SequenceNumber = sequenceNumbersByMediaId[mediaId],
        }).ToList();

        await dbContext.Set<CollectionItem>().AddRangeAsync(items);
        await SaveAsync();
        await updateNotifier.NotifyCollectionContentsUpdated(collectionId);
    }

    public async Task SetCollectionImageGroupSizeAsync(long collectionId, int imageGroupSize)
    {
        if (imageGroupSize < 1)
            throw new ArgumentOutOfRangeException(nameof(imageGroupSize));

        var collection = await dbContext.Collections.FindAsync(collectionId) ??
                         throw new ArgumentException("Collection not found");
        var activeItemCount = await dbContext.Set<CollectionItem>()
            .Where(ci => ci.CollectionId == collectionId && !ci.MediaFile.IsDeleted)
            .CountAsync();
        if (activeItemCount % imageGroupSize != 0)
        {
            throw new InvalidOperationException(
                $"The collection has {activeItemCount} active images, which is not divisible by {imageGroupSize}.");
        }

        collection.ImageGroupSize = imageGroupSize;
        await SaveAsync();
        await updateNotifier.NotifyCollectionUpdated(collectionId);
    }

    public async Task ReorderCollection(long collectionId, List<long> newImageOrderIds)
    {
        var collection = await dbContext.Collections
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.Id == collectionId);

        if (collection == null)
            throw new ArgumentException("Collection not found");

        var itemsByMediaId = collection.Items.ToDictionary(ci => ci.MediaFileId);
        int nextSequence = 0;

        // Assign sequence numbers to items in the new order
        foreach (var mediaId in newImageOrderIds)
        {
            if (itemsByMediaId.Remove(mediaId, out var item))
            {
                item.SequenceNumber = nextSequence++;
            }
        }

        // Assign sequence numbers to items NOT in the new order (move to end)
        // Sort remaining by old sequence number to preserve relative order
        foreach (var item in itemsByMediaId.Values.OrderBy(ci => ci.SequenceNumber))
        {
            item.SequenceNumber = nextSequence++;
        }

        collection.UpdatedAt = DateTime.UtcNow;
        await SaveAsync();
        await updateNotifier.NotifyCollectionUpdated(collectionId);
    }

    public async Task RemoveMediaFromCollection(long mediaId, long collectionId)
    {
        var item = await dbContext.Set<CollectionItem>().FirstOrDefaultAsync(ci =>
            ci.CollectionId == collectionId && ci.MediaFileId == mediaId);

        if (item == null)
            return;

        dbContext.Set<CollectionItem>().Remove(item);
        await SaveAsync();
        await updateNotifier.NotifyCollectionContentsUpdated(collectionId);
    }

    public async Task<CollectionMediaRemovalPreview> PreviewCollectionMediaRemovalAsync(long collectionId,
        List<long> mediaIds)
    {
        var selectedIds = mediaIds.Distinct().ToList();
        var collectionMediaIds = await dbContext.Set<CollectionItem>()
            .Where(item => item.CollectionId == collectionId && selectedIds.Contains(item.MediaFileId))
            .Select(item => item.MediaFileId)
            .ToListAsync();

        var orphanedMediaIds = await dbContext.Set<CollectionItem>()
            .Where(item => collectionMediaIds.Contains(item.MediaFileId) && item.CollectionId != collectionId)
            .Select(item => item.MediaFileId)
            .Distinct()
            .ToListAsync();

        return new CollectionMediaRemovalPreview
        {
            OrphanedMediaIds = collectionMediaIds.Except(orphanedMediaIds).ToList(),
        };
    }

    public async Task<CollectionMediaRemovalResult> RemoveMediaFromCollectionAsync(long collectionId,
        List<long> mediaIds)
    {
        _ = await dbContext.Collections.FindAsync(collectionId) ?? throw new ArgumentException("Collection not found");
        var selectedIds = mediaIds.Distinct().ToHashSet();
        var items = await dbContext.Set<CollectionItem>()
            .Where(item => item.CollectionId == collectionId && selectedIds.Contains(item.MediaFileId))
            .ToListAsync();

        var result = new CollectionMediaRemovalResult
        {
            CollectionId = collectionId,
            RemovedItems = items.Select(item => new CollectionMediaRemovalItem
            {
                MediaId = item.MediaFileId,
                SequenceNumber = item.SequenceNumber,
            }).ToList(),
        };

        if (items.Count == 0)
            return result;

        dbContext.Set<CollectionItem>().RemoveRange(items);
        var removedMediaIds = items.Select(item => item.MediaFileId).ToList();
        var nonOrphanedMediaIds = await dbContext.Set<CollectionItem>()
            .Where(item => removedMediaIds.Contains(item.MediaFileId) && item.CollectionId != collectionId)
            .Select(item => item.MediaFileId)
            .Distinct()
            .ToListAsync();
        var uncategorizedIds = removedMediaIds.Except(nonOrphanedMediaIds).ToList();
        if (collectionId == Collection.UncategorizedCollectionId)
            uncategorizedIds.Clear();

        if (uncategorizedIds.Count > 0)
        {
            var nextSequenceNumber = await GetNextCollectionSequenceNumberAsync(Collection.UncategorizedCollectionId);
            dbContext.Set<CollectionItem>().AddRange(uncategorizedIds.Select((mediaId, index) => new CollectionItem
            {
                CollectionId = Collection.UncategorizedCollectionId,
                MediaFileId = mediaId,
                SequenceNumber = nextSequenceNumber + index,
            }));
            result.AddedToUncategorizedMediaIds = uncategorizedIds;
        }

        await SaveAsync();
        await updateNotifier.NotifyCollectionContentsUpdated(collectionId);
        if (result.AddedToUncategorizedMediaIds.Count > 0)
            await updateNotifier.NotifyCollectionContentsUpdated(Collection.UncategorizedCollectionId);
        return result;
    }

    public async Task UndoCollectionMediaRemovalAsync(CollectionMediaRemovalResult removal)
    {
        var collection = await dbContext.Collections.IgnoreQueryFilters().Include(item => item.Folders)
                             .FirstOrDefaultAsync(item => item.Id == removal.CollectionId) ??
                         throw new ArgumentException("Collection not found");
        var mediaIds = removal.RemovedItems.Select(item => item.MediaId).ToList();
        var existingItems = await dbContext.Set<CollectionItem>()
            .IgnoreQueryFilters()
            .Where(item => item.CollectionId == removal.CollectionId && mediaIds.Contains(item.MediaFileId))
            .Select(item => item.MediaFileId)
            .ToHashSetAsync();
        var addedToUncategorized = removal.AddedToUncategorizedMediaIds.ToHashSet();
        var uncategorizedItems = await dbContext.Set<CollectionItem>()
            .Where(item => item.CollectionId == Collection.UncategorizedCollectionId &&
                           addedToUncategorized.Contains(item.MediaFileId))
            .ToListAsync();
        dbContext.Set<CollectionItem>().RemoveRange(uncategorizedItems);

        var itemsToRestore = removal.RemovedItems
            .Where(item => !existingItems.Contains(item.MediaId))
            .Select(item => new CollectionItem
            {
                CollectionId = removal.CollectionId,
                MediaFileId = item.MediaId,
                SequenceNumber = item.SequenceNumber,
            })
            .ToList();
        dbContext.Set<CollectionItem>().AddRange(itemsToRestore);
        if (removal.CollectionWasDeleted)
        {
            collection.IsDeleted = false;
            collection.UpdatedAt = DateTime.UtcNow;
            var deletedMedia = await dbContext.MediaFiles
                .IgnoreQueryFilters()
                .Where(media => removal.DeletedMediaIds.Contains(media.Id))
                .ToListAsync();
            foreach (var media in deletedMedia)
            {
                media.IsDeleted = false;
                media.UpdatedAt = DateTime.UtcNow;
            }
        }

        await SaveAsync();
        await updateNotifier.NotifyCollectionContentsUpdated(collection.Id);
        if (removal.CollectionWasDeleted)
        {
            foreach (var folder in collection.Folders)
                await updateNotifier.NotifyMediaFolderContentsUpdated(folder.Id);
            foreach (var mediaId in removal.DeletedMediaIds)
                await updateNotifier.NotifyMediaUpdated(mediaId);
        }

        if (uncategorizedItems.Count > 0)
            await updateNotifier.NotifyCollectionContentsUpdated(Collection.UncategorizedCollectionId);
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

        if (folder.Id == MediaFolder.RootFolderId)
            return "/";

        var builder = new StringBuilder();

        while (folder != null && folder.Id != MediaFolder.RootFolderId)
        {
            builder.Insert(0, folder.Name);
            builder.Insert(0, '/');

            var currentFolder = folder;
            folder = await dbContext.MediaFolders
                .Where(f => f.SubFolders.Any(sf => sf.Id == currentFolder.Id))
                .FirstOrDefaultAsync();
        }

        return builder.ToString();
    }

    public async Task<List<FolderPathDTO>> GetCollectionFolderPaths(long collectionId)
    {
        var collection = await dbContext.Collections.Include(c => c.Folders)
            .FirstOrDefaultAsync(c => c.Id == collectionId);
        if (collection == null)
            throw new Exception("Collection not found");

        var result = new List<FolderPathDTO>();
        foreach (var folder in collection.Folders)
        {
            result.Add(new FolderPathDTO
            {
                Id = folder.Id,
                Path = await GetMediaFolderPath(folder)
            });
        }

        return result;
    }

    public async Task<List<FolderPathDTO>> GetFolderParentFolderPaths(long folderId)
    {
        var folder = await dbContext.MediaFolders.Include(f => f.Parents)
            .FirstOrDefaultAsync(f => f.Id == folderId);
        if (folder == null)
            throw new Exception("Folder not found");

        var result = new List<FolderPathDTO>();
        foreach (var parent in folder.Parents)
        {
            result.Add(new FolderPathDTO
            {
                Id = parent.Id,
                Path = await GetMediaFolderPath(parent)
            });
        }

        return result;
    }

    public async Task<string> GetMediaFolderPath(long folderId)
    {
        var folder = await dbContext.MediaFolders.FindAsync(folderId);
        return await GetMediaFolderPath(folder);
    }

    public async Task<MediaFolder> CreateMediaFolderAsync(string folderName, long parentId)
    {
        var trimmedName = folderName.TrimOrThrowIfEmpty();
        if (trimmedName.Length > 200)
            throw new ArgumentException("Folder name is too long");

        var folder = new MediaFolder(trimmedName);

        var parent = await dbContext.MediaFolders.FindAsync(parentId);
        if (parent == null)
            throw new Exception("Parent folder ID not found");

        if (await dbContext.MediaFolders.AnyAsync(existing =>
                existing.NameLowerCase == folder.NameLowerCase &&
                existing.Parents.Any(parentFolder => parentFolder.Id == parentId)))
        {
            throw new InvalidOperationException("A folder with that name already exists here");
        }

        folder.Parents.Add(parent);

        await dbContext.MediaFolders.AddAsync(folder);
        await SaveAsync();
        await updateNotifier.NotifyMediaFoldersUpdated();
        return folder;
    }

    public async Task<bool> IsMediaSafeToDeleteAsync(long mediaId)
    {
        var media = await dbContext.MediaFiles
            .Include(m => m.InCollections)
            .FirstOrDefaultAsync(m => m.Id == mediaId);

        if (media == null)
            return false;

        // For now, if it's in any collection, it's not safe to delete

        return true;
    }

    public async Task DeleteMediaAsync(long mediaId)
    {
        var media = await dbContext.MediaFiles.FirstOrDefaultAsync(m => m.Id == mediaId);
        if (media == null || media.IsDeleted)
            return;

        media.IsDeleted = true;
        media.UpdatedAt = DateTime.UtcNow;
        await SaveAsync();
        await updateNotifier.NotifyMediaUpdated(mediaId);
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
        media.UpdatedAt = DateTime.UtcNow;
        await SaveAsync();
        await updateNotifier.NotifyMediaUpdated(mediaId);
    }

    public async Task PurgeMediaAsync(long mediaId)
    {
        var media = await dbContext.MediaFiles.FindAsync(mediaId) ?? throw new ArgumentException("Media not found");
        if (!media.IsDeleted)
            throw new InvalidOperationException("Cannot purge non-deleted media");

        var settings = await GetAppSettingsAsync();
        var baseStorage = settings.LocalMediaStorageLocation ?? dataFolderService.GetDataFolderPath();

        // Delete physical files
        mediaProcessingService.NotifyMediaFileChanged(media, baseStorage);

        var originalPath = Path.Join(baseStorage, media.PathRelativeToStorage());
        if (File.Exists(originalPath))
        {
            try
            {
                File.Delete(originalPath);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to delete original media file during purge");
            }
        }
        else
        {
            logger.LogWarning("Original media file {Path} does not exist (when purging)", originalPath);
        }

        var croppedPath = Path.Join(baseStorage, media.CroppedPathRelativeToStorage());
        if (File.Exists(croppedPath))
        {
            try
            {
                File.Delete(croppedPath);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to delete physical cropped media file {Path}", croppedPath);
            }
        }

        dbContext.MediaFiles.Remove(media);
        await SaveAsync();
        await updateNotifier.NotifyMediaUpdated(mediaId);
    }

    public async Task<List<MediaFile>> GetDeletedMediaAsync(int limit)
    {
        return await dbContext.MediaFiles
            .Where(m => m.IsDeleted)
            .OrderByDescending(m => m.UpdatedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<MediaFolder>> GetDeletedMediaFoldersAsync(int limit)
    {
        return await dbContext.MediaFolders
            .Where(f => f.IsDeleted)
            .OrderByDescending(f => f.UpdatedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<Collection>> GetDeletedCollectionsAsync(int limit)
    {
        return await dbContext.Collections
            .Where(c => c.IsDeleted)
            .OrderByDescending(c => c.UpdatedAt)
            .Take(limit)
            .ToListAsync();
    }

    async Task<List<MediaFileDTO>> IClientDatabaseService.GetDeletedMediaAsync(int limit)
    {
        var media = await GetDeletedMediaAsync(limit);
        return media.ConvertToDTO<MediaFile, MediaFileDTO>();
    }

    async Task<List<MediaFolderDTO>> IClientDatabaseService.GetDeletedMediaFoldersAsync(int limit)
    {
        var folders = await GetDeletedMediaFoldersAsync(limit);
        return folders.ConvertToDTO<MediaFolder, MediaFolderDTO>();
    }

    async Task<List<CollectionDTO>> IClientDatabaseService.GetDeletedCollectionsAsync(int limit)
    {
        var collections = await GetDeletedCollectionsAsync(limit);
        return collections.ConvertToDTO<Collection, CollectionDTO>();
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
        return (await GetMediaFileSiblingsAsync(mediaId)).ConvertToDTO<MediaFile, MediaFileDTO>();
    }

    public async Task<MediaFileDTO> CreateMediaFileAsync(MediaFileDTO mediaFile, long collectionId)
    {
        var newMedia = new MediaFile(mediaFile.OriginalFileName, mediaFile.Hash)
        {
            MediaType = mediaFile.MediaType,
            Width = mediaFile.Width,
            Height = mediaFile.Height,
            FrameCount = mediaFile.FrameCount,
            FramesPerSecond = mediaFile.FramesPerSecond,
            ParentMediaId = mediaFile.ParentMediaId
        };

        var result = await CreateMediaAsync(newMedia, collectionId);
        return result.GetDTO();
    }

    public async Task SaveMediaFileAsync(MediaFileDTO media)
    {
        var existing = await dbContext.MediaFiles.FindAsync(media.Id) ??
                       throw new ArgumentException("Media not found");

        existing.IsDeleted = media.IsDeleted;
        existing.CropLeft = media.CropLeft;
        existing.CropTop = media.CropTop;
        existing.CropRight = media.CropRight;
        existing.CropBottom = media.CropBottom;
        existing.UpdatedAt = DateTime.UtcNow;

        await SaveAsync();
    }

    public async Task<List<MediaFolder>> GetMediaFoldersAsync(long? limitToParent)
    {
        if (limitToParent != null)
        {
            return await dbContext.MediaFolders.Where(f => f.Parents.Any(p => p.Id == limitToParent)).ToListAsync();
        }

        return await dbContext.MediaFolders.Where(f => !f.Parents.Any()).ToListAsync();
    }

    public async Task<MediaFolder?> GetMediaFolderAsync(long id)
    {
        return await dbContext.MediaFolders.Include(f => f.Parents).FirstOrDefaultAsync(f => f.Id == id);
    }

    public async Task<MediaFolder?> GetMediaFolderAsync(string name, long? parentFolderId)
    {
        if (parentFolderId == null)
        {
            // Note this is not even in the root folder, so this is kind of invalid data if this finds anything
            return await dbContext.MediaFolders.FirstOrDefaultAsync(f =>
                f.Name == name && !f.Parents.Any());
        }

        return await dbContext.MediaFolders.FirstOrDefaultAsync(f =>
            f.Name == name && f.Parents.Any(p => p.Id == parentFolderId));
    }

    public async Task SaveMediaFolderAsync(MediaFolder folder)
    {
        await SaveAsync();
        await updateNotifier.NotifyMediaFoldersUpdated();
    }

    public async Task<MediaFolder?> GetMediaFolderFromPathAsync(string path)
    {
        return await MediaFolder.GetOrCreateAtPath(path.TrimEnd(), this, false);
    }

    public async Task<Tuple<List<CollectionDTO>, int>> GetFolderCollections(long folderId, int page, int pageSize)
    {
        var query = dbContext.Collections.Where(c => c.Folders.Any(f => f.Id == folderId));
        var total = await query.CountAsync();
        var items = await query.OrderBy(c => c.Name)
            .Skip(page * pageSize)
            .Take(pageSize)
            .Include(c => c.Folders)
            .Select(c => c.GetDTO())
            .ToListAsync();

        return new Tuple<List<CollectionDTO>, int>(items, total);
    }

    public async Task<Tuple<List<MediaFileDTO>, int>> GetCollectionContents(long collectionId, int page, int pageSize,
        CollectionSortColumn sortColumn = CollectionSortColumn.CollectionOrder,
        SortDirection sortDirection = SortDirection.Ascending, string? search = null)
    {
        var query = dbContext.Set<CollectionItem>()
            .Where(ci => ci.CollectionId == collectionId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLowerInvariant();
            query = query.Where(ci => ci.MediaFile.NameLowerCase.Contains(searchLower));
        }

        var total = await query.CountAsync();

        IQueryable<CollectionItem> sortedQuery = sortColumn switch
        {
            CollectionSortColumn.Name => sortDirection == SortDirection.Ascending
                ? query.OrderBy(ci => ci.MediaFile.NameLowerCase)
                : query.OrderByDescending(ci => ci.MediaFile.NameLowerCase),
            CollectionSortColumn.ImportedAt => sortDirection == SortDirection.Ascending
                ? query.OrderBy(ci => ci.MediaFile.ImportedAt)
                : query.OrderByDescending(ci => ci.MediaFile.ImportedAt),
            CollectionSortColumn.LastViewed => sortDirection == SortDirection.Ascending
                ? query.OrderBy(ci => ci.MediaFile.LastViewed)
                : query.OrderByDescending(ci => ci.MediaFile.LastViewed),
            _ => sortDirection == SortDirection.Ascending
                ? query.OrderBy(ci => ci.SequenceNumber)
                : query.OrderByDescending(ci => ci.SequenceNumber),
        };

        var items = await sortedQuery
            .Skip(page * pageSize)
            .Take(pageSize)
            .Select(ci => ci.MediaFile.GetDTO())
            .ToListAsync();

        return new Tuple<List<MediaFileDTO>, int>(items, total);
    }

    public async Task<List<MediaFile>> GetCollectionContents(long collectionId)
    {
        return await dbContext.Set<CollectionItem>()
            .Where(ci => ci.CollectionId == collectionId)
            .OrderBy(ci => ci.SequenceNumber)
            .Select(ci => ci.MediaFile)
            .ToListAsync();
    }

    async Task<List<MediaFileDTO>> IClientDatabaseService.GetCollectionContents(long collectionId)
    {
        return (await GetCollectionContents(collectionId)).ConvertToDTO<MediaFile, MediaFileDTO>();
    }

    public async Task<List<Collection>> GetCollectionsInFolderAsync(long folderId)
    {
        return await dbContext.Collections.Where(c => c.Folders.Any(f => f.Id == folderId)).ToListAsync();
    }

    public async Task<Collection?> GetCollectionByNameAndFolder(string name, long folderId)
    {
        return await dbContext.Collections.FirstOrDefaultAsync(c =>
            c.Name == name && c.Folders.Any(f => f.Id == folderId));
    }

    public async Task<Collection?> GetCollectionAsync(long id)
    {
        return await dbContext.Collections.Include(c => c.Folders).FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<MediaFile?> GetCollectionPreviewMediaAsync(long collectionId)
    {
        var collection = await dbContext.Collections.FirstOrDefaultAsync(c => c.Id == collectionId);
        if (collection == null)
            return null;

        if (collection.PreviewMediaId is { } previewMediaId)
        {
            return await dbContext.MediaFiles.FirstOrDefaultAsync(m => m.Id == previewMediaId);
        }

        return await dbContext.Set<CollectionItem>()
            .Where(item => item.CollectionId == collectionId)
            .OrderBy(item => item.SequenceNumber)
            .Select(item => item.MediaFile)
            .FirstOrDefaultAsync();
    }

    public async Task SaveCollectionAsync(Collection collection)
    {
        await SaveAsync();
        await updateNotifier.NotifyCollectionUpdated(collection.Id);
    }

    public async Task DeleteMediaFolderAsync(long folderId)
    {
        var folder = await dbContext.MediaFolders.FindAsync(folderId) ??
                     throw new ArgumentException("Folder not found");
        folder.IsDeleted = true;
        folder.UpdatedAt = DateTime.UtcNow;
        await SaveAsync();
        await updateNotifier.NotifyMediaFoldersUpdated();
    }

    public async Task RestoreMediaFolderAsync(long folderId)
    {
        var folder = await dbContext.MediaFolders.FindAsync(folderId) ??
                     throw new ArgumentException("Folder not found");
        folder.IsDeleted = false;
        folder.UpdatedAt = DateTime.UtcNow;
        await SaveAsync();
        await updateNotifier.NotifyMediaFoldersUpdated();
    }

    public async Task PurgeMediaFolderAsync(long folderId)
    {
        var folder = await dbContext.MediaFolders.FindAsync(folderId) ??
                     throw new ArgumentException("Folder not found");
        if (!folder.IsDeleted)
            throw new InvalidOperationException("Cannot purge non-deleted folder");

        dbContext.MediaFolders.Remove(folder);
        await SaveAsync();
        await updateNotifier.NotifyMediaFoldersUpdated();
    }

    public async Task DeleteCollectionAsync(long collectionId)
    {
        if (collectionId == Collection.UncategorizedCollectionId)
            throw new InvalidOperationException("The Uncategorized collection cannot be deleted");

        var collection =
            await dbContext.Collections.IgnoreQueryFilters().Include(c => c.Folders)
                .FirstOrDefaultAsync(c => c.Id == collectionId) ??
            throw new ArgumentException("Collection not found");
        collection.IsDeleted = true;
        collection.UpdatedAt = DateTime.UtcNow;
        await SaveAsync();
        foreach (var folder in collection.Folders)
        {
            await updateNotifier.NotifyMediaFolderContentsUpdated(folder.Id);
        }
    }

    public async Task<int> GetCollectionOrphanedMediaCountAsync(long collectionId)
    {
        if (collectionId == Collection.UncategorizedCollectionId)
            return 0;

        var mediaIds = await dbContext.Set<CollectionItem>()
            .Where(item => item.CollectionId == collectionId)
            .Select(item => item.MediaFileId)
            .Distinct()
            .ToListAsync();
        var mediaInOtherCollections = await dbContext.Set<CollectionItem>()
            .Where(item => mediaIds.Contains(item.MediaFileId) && item.CollectionId != collectionId)
            .Select(item => item.MediaFileId)
            .Distinct()
            .ToListAsync();
        return mediaIds.Except(mediaInOtherCollections).Count();
    }

    public async Task<CollectionMediaRemovalResult> DeleteCollectionAndImagesAsync(long collectionId)
    {
        if (collectionId == Collection.UncategorizedCollectionId)
            throw new InvalidOperationException("The Uncategorized collection cannot be deleted");

        var collection = await dbContext.Collections.Include(item => item.Folders)
                             .FirstOrDefaultAsync(item => item.Id == collectionId) ??
                         throw new ArgumentException("Collection not found");
        var items = await dbContext.Set<CollectionItem>()
            .Where(item => item.CollectionId == collectionId)
            .ToListAsync();
        var mediaIds = items.Select(item => item.MediaFileId).Distinct().ToList();
        var mediaFiles = await dbContext.MediaFiles
            .Where(media => mediaIds.Contains(media.Id) && !media.IsDeleted)
            .ToListAsync();
        collection.IsDeleted = true;
        collection.UpdatedAt = DateTime.UtcNow;
        foreach (var media in mediaFiles)
        {
            media.IsDeleted = true;
            media.UpdatedAt = DateTime.UtcNow;
        }

        await SaveAsync();
        foreach (var folder in collection.Folders)
            await updateNotifier.NotifyMediaFolderContentsUpdated(folder.Id);
        foreach (var media in mediaFiles)
            await updateNotifier.NotifyMediaUpdated(media.Id);

        return new CollectionMediaRemovalResult
        {
            CollectionId = collectionId,
            RemovedItems = items.Select(item => new CollectionMediaRemovalItem
            {
                MediaId = item.MediaFileId,
                SequenceNumber = item.SequenceNumber,
            }).ToList(),
            DeletedMediaIds = mediaFiles.Select(media => media.Id).ToList(),
            CollectionWasDeleted = true,
        };
    }

    public async Task DeleteCollectionAsync(Collection collection)
    {
        await DeleteCollectionAsync(collection.Id);
    }

    public async Task RestoreCollectionAsync(long collectionId)
    {
        var collection =
            await dbContext.Collections.IgnoreQueryFilters().Include(c => c.Folders)
                .FirstOrDefaultAsync(c => c.Id == collectionId) ??
            throw new ArgumentException("Collection not found");
        collection.IsDeleted = false;
        collection.UpdatedAt = DateTime.UtcNow;
        await SaveAsync();
        foreach (var folder in collection.Folders)
        {
            await updateNotifier.NotifyMediaFolderContentsUpdated(folder.Id);
        }
    }

    public async Task PurgeCollectionAsync(long collectionId)
    {
        var collection =
            await dbContext.Collections.IgnoreQueryFilters().Include(c => c.Folders)
                .FirstOrDefaultAsync(c => c.Id == collectionId) ??
            throw new ArgumentException("Collection not found");
        if (!collection.IsDeleted)
            throw new InvalidOperationException("Cannot purge non-deleted collection");

        var folderIds = collection.Folders.Select(f => f.Id).ToList();
        var activeMediaIds = await dbContext.Set<CollectionItem>()
            .IgnoreQueryFilters()
            .Where(item => item.CollectionId == collectionId && !item.MediaFile.IsDeleted)
            .Select(item => item.MediaFileId)
            .ToListAsync();
        var existingUncategorizedMediaIds = await dbContext.Set<CollectionItem>()
            .Where(item => activeMediaIds.Contains(item.MediaFileId) &&
                           item.CollectionId == Collection.UncategorizedCollectionId)
            .Select(item => item.MediaFileId)
            .ToHashSetAsync();
        var mediaToCategorize = activeMediaIds.Except(existingUncategorizedMediaIds).ToList();
        if (mediaToCategorize.Count > 0)
        {
            var nextSequenceNumber = await GetNextCollectionSequenceNumberAsync(Collection.UncategorizedCollectionId);
            dbContext.Set<CollectionItem>().AddRange(mediaToCategorize.Select((mediaId, index) => new CollectionItem
            {
                CollectionId = Collection.UncategorizedCollectionId,
                MediaFileId = mediaId,
                SequenceNumber = nextSequenceNumber + index,
            }));
        }

        dbContext.Collections.Remove(collection);
        await SaveAsync();

        foreach (var folderId in folderIds)
        {
            await updateNotifier.NotifyMediaFolderContentsUpdated(folderId);
        }
    }

    public async Task<MediaFile?> GetMediaByHashAsync(string hash)
    {
        return await dbContext.MediaFiles.FirstOrDefaultAsync(m => m.Hash == hash);
    }

    public async Task<MediaFile?> GetMediaByIdAsync(long id)
    {
        return await dbContext.MediaFiles.FindAsync(id);
    }

    public async Task<List<long>> GetAllMediaFileIdsAsync()
    {
        return await dbContext.MediaFiles.Select(m => m.Id).ToListAsync();
    }

    public async Task<List<long>> GetOrphanedMediaFilesAsync()
    {
        return await dbContext.MediaFiles
            .Where(m => !m.InCollections.Any())
            .Select(m => m.Id)
            .ToListAsync();
    }

    public async Task<List<Collection>> GetOrphanedCollectionsAsync()
    {
        return await dbContext.Collections
            .Include(c => c.Folders)
            .Where(c => !c.Folders.Any())
            .ToListAsync();
    }

    public async Task<List<MediaFolder>> GetOrphanedMediaFoldersAsync()
    {
        return await dbContext.MediaFolders
            .Include(f => f.Parents)
            .Where(f => f.Id != MediaFolder.RootFolderId && !f.Parents.Any())
            .ToListAsync();
    }

    public async Task<int> GetNextCollectionSequenceNumberAsync(long collectionId)
    {
        return await dbContext.Set<CollectionItem>()
            .Where(ci => ci.CollectionId == collectionId)
            .Select(ci => ci.SequenceNumber)
            .DefaultIfEmpty(0)
            .MaxAsync() + 1;
    }

    public async Task SaveMediaFileAsync(MediaFile mediaFile)
    {
        await SaveAsync();
        await updateNotifier.NotifyMediaUpdated(mediaFile.Id);
    }

    public async Task<UploadSection> GetOrCreateUploadSectionAsync(string? sectionName)
    {
        if (string.IsNullOrWhiteSpace(sectionName))
        {
            var selected = await dbContext.UploadSections.FirstOrDefaultAsync(s => s.Selected);
            if (selected != null)
                return selected;

            sectionName = "";
        }

        var lowercase = sectionName.ToLowerInvariant();
        var existing = await dbContext.UploadSections.FirstOrDefaultAsync(s => s.NameLowercase == lowercase);

        if (existing != null)
            return existing;

        // Insert at the start
        var minIndex = await dbContext.UploadSections.Select(s => s.DisplayIndex).DefaultIfEmpty(0).MinAsync();

        var newSection = new UploadSection(sectionName)
        {
            DisplayIndex = minIndex - 1,

            // And auto-select if nothing is selected
            Selected = !await dbContext.UploadSections.AnyAsync(s => s.Selected),
        };

        await dbContext.UploadSections.AddAsync(newSection);
        await SaveAsync();

        // TODO: notice event about sections being added

        return newSection;
    }

    public async Task AddMediaToUploadSectionAsync(long mediaId, long sectionId, int index)
    {
        var item = new UploadSectionItem
        {
            UploadSectionId = sectionId,
            MediaFileId = mediaId,
            Index = index,
        };

        await dbContext.UploadSectionItems.AddAsync(item);

        // TODO: notice event about section items being changed

        await SaveAsync();
    }

    public async Task<int> GetNextUploadSectionIndexAsync(long sectionId)
    {
        return await dbContext.UploadSectionItems
            .Where(i => i.UploadSectionId == sectionId)
            .Select(i => i.Index)
            .DefaultIfEmpty(0)
            .MaxAsync() + 1;
    }

    public async Task SetMediaTemporaryStatusAsync(long mediaId, bool isTemporary)
    {
        var media =
            await dbContext.MediaFiles.Include(m => m.InCollections).FirstOrDefaultAsync(m => m.Id == mediaId) ??
            throw new ArgumentException("Media not found");

        if (isTemporary)
        {
            if (media.InCollections.Count > 0)
                throw new InvalidOperationException("Cannot mark media as temporary if it is in a collection already");
        }

        media.IsTemporary = isTemporary;
        await SaveAsync();
    }

    public async Task BumpUploadSectionLastImportedAsync(long sectionId)
    {
        var section = await dbContext.UploadSections.FindAsync(sectionId) ??
                      throw new ArgumentException("Section not found");
        section.LastImported = DateTime.UtcNow;
        section.BumpUpdatedAtTime();
        await SaveAsync();
    }

    public async Task<MediaFile> CreateMediaAsync(MediaFile mediaItem, long collectionId)
    {
        var collection = await dbContext.Collections.FindAsync(collectionId) ??
                         throw new ArgumentException("Collection not found");
        var activeItemCount = await dbContext.Set<CollectionItem>()
            .Where(ci => ci.CollectionId == collectionId && !ci.MediaFile.IsDeleted)
            .CountAsync();
        if ((activeItemCount + 1) % collection.ImageGroupSize != 0)
        {
            throw new InvalidOperationException(
                $"The collection requires images to be added in groups of {collection.ImageGroupSize}.");
        }

        await dbContext.MediaFiles.AddAsync(mediaItem);
        await SaveAsync();

        var sequenceNumber = await GetNextCollectionSequenceNumberAsync(collectionId);

        await AddMediaToCollection([mediaItem.Id], collectionId, sequenceNumber);

        return mediaItem;
    }

    public async Task<MediaFile> CreateMediaAsync(MediaFile mediaItem, string? sectionName)
    {
        await dbContext.MediaFiles.AddAsync(mediaItem);
        await SaveAsync();

        var section = await GetOrCreateUploadSectionAsync(sectionName);

        // We are adding a new item so mark the last usage as updated (the following methods will call DB save)
        section.UpdatedAt = DateTime.UtcNow;

        var index = await GetNextUploadSectionIndexAsync(section.Id);

        await AddMediaToUploadSectionAsync(mediaItem.Id, section.Id, index);

        return mediaItem;
    }

    public async Task<List<MediaFile>> GetEligibleMediaFilesForPurgeAsync(TimeSpan timeSinceDeletion)
    {
        var cutoffTime = DateTime.UtcNow - timeSinceDeletion;

        return await dbContext.MediaFiles
            .Where(m => m.IsDeleted && m.UpdatedAt <= cutoffTime)
            .ToListAsync();
    }

    public async Task<List<Collection>> GetEligibleCollectionsForPurgeAsync(TimeSpan timeSinceDeletion)
    {
        var cutoffTime = DateTime.UtcNow - timeSinceDeletion;
        return await dbContext.Collections
            .IgnoreQueryFilters()
            .Where(collection => collection.IsDeleted && collection.UpdatedAt <= cutoffTime)
            .ToListAsync();
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

    public async Task<bool> SetMediaRatingAsync(long mediaId, bool isFavorited, int stars)
    {
        var media = await dbContext.MediaFiles.FindAsync(mediaId) ?? throw new ArgumentException("Media not found");

        if (media.IsFavorited == isFavorited && media.Stars == stars)
            return false;

        media.IsFavorited = isFavorited;
        media.Stars = stars;
        await SaveMediaFileAsync(media);
        return true;
    }

    // Tags
    public async Task<long> CreateTagAsync(string name, TagCategory category)
    {
        var tag = new Tag(name.TrimOrThrowIfEmpty().ToLowerInvariant(), category);
        await dbContext.Tags.AddAsync(tag);
        await SaveAsync();
        await updateNotifier.NotifyTagsUpdated();
        return tag.Id;
    }

    public async Task UpdateTagAsync(long id, string? name, string? description, TagCategory? category,
        long? exampleMediaId)
    {
        var tag = await dbContext.Tags.FindAsync(id) ?? throw new ArgumentException("Tag not found");

        if (name != null)
            tag.Name = name.TrimOrThrowIfEmpty().ToLowerInvariant();

        if (description != null)
            tag.Description = description;

        if (category != null)
            tag.Category = category.Value;

        if (exampleMediaId != null)
            tag.ExampleMediaId = exampleMediaId == -1 ? null : exampleMediaId;

        await SaveAsync();
        await updateNotifier.NotifyTagUpdated(id);
        await updateNotifier.NotifyTagsUpdated();
    }

    public async Task DeleteTagAsync(long id)
    {
        var tag = await dbContext.Tags.FindAsync(id) ?? throw new ArgumentException("Tag not found");
        tag.IsDeleted = true;
        tag.UpdatedAt = DateTime.UtcNow;
        await SaveAsync();
        await updateNotifier.NotifyTagsUpdated();
    }

    public async Task<long> CreateTagModifierAsync(string name)
    {
        var modifier = new TagModifier(name.TrimOrThrowIfEmpty().ToLowerInvariant());
        await dbContext.TagModifiers.AddAsync(modifier);
        await SaveAsync();
        await updateNotifier.NotifyTagModifiersUpdated();
        return modifier.Id;
    }

    public async Task UpdateTagModifierAsync(long id, string? name, string? description)
    {
        var modifier = await dbContext.TagModifiers.FindAsync(id) ?? throw new ArgumentException("Modifier not found");

        if (name != null)
            modifier.Name = name.TrimOrThrowIfEmpty().ToLowerInvariant();

        if (description != null)
            modifier.Description = description;

        modifier.UpdatedAt = DateTime.UtcNow;
        await SaveAsync();
        await updateNotifier.NotifyTagModifiersUpdated();
    }

    public async Task DeleteTagModifierAsync(long id)
    {
        var modifier = await dbContext.TagModifiers.FindAsync(id) ?? throw new ArgumentException("Modifier not found");
        modifier.IsDeleted = true;
        modifier.UpdatedAt = DateTime.UtcNow;
        await SaveAsync();
        await updateNotifier.NotifyTagModifiersUpdated();
    }

    public async Task<List<string>> GetTagAliasesAsync(long tagId)
    {
        return await dbContext.TagAliases
            .Where(a => a.TagId == tagId)
            .Select(a => a.Name)
            .ToListAsync();
    }

    public async Task<List<TagDTO>> GetTagImpliesAsync(long tagId)
    {
        return await dbContext.TagImplies
            .Where(i => i.PrimaryTagId == tagId)
            .Select(i => i.ToApplyTag.GetDTO())
            .ToListAsync();
    }

    public async Task CreateTagAliasAsync(long tagId, string alias)
    {
        var tagAlias = new TagAlias(alias.TrimOrThrowIfEmpty().ToLowerInvariant(), tagId);
        await dbContext.TagAliases.AddAsync(tagAlias);
        await SaveAsync();
        await updateNotifier.NotifyTagUpdated(tagId);
    }

    public async Task DeleteTagAliasAsync(long tagId, string alias)
    {
        var tagAlias =
            await dbContext.TagAliases.FirstOrDefaultAsync(a => a.TagId == tagId && a.Name == alias.ToLowerInvariant());
        if (tagAlias != null)
        {
            dbContext.TagAliases.Remove(tagAlias);
            await SaveAsync();
            await updateNotifier.NotifyTagUpdated(tagId);
        }
    }

    public async Task AddTagImplicationAsync(long tagId, long impliedTagId)
    {
        if (tagId == impliedTagId)
            return;

        var alreadyExists =
            await dbContext.TagImplies.AnyAsync(i => i.PrimaryTagId == tagId && i.ToApplyTagId == impliedTagId);
        if (alreadyExists)
            return;

        var imply = new TagImply(tagId, impliedTagId);
        await dbContext.TagImplies.AddAsync(imply);
        await SaveAsync();
        await updateNotifier.NotifyTagUpdated(tagId);
    }

    public async Task RemoveTagImplicationAsync(long tagId, long impliedTagId)
    {
        var imply = await dbContext.TagImplies.FirstOrDefaultAsync(i =>
            i.PrimaryTagId == tagId && i.ToApplyTagId == impliedTagId);
        if (imply != null)
        {
            dbContext.TagImplies.Remove(imply);
            await SaveAsync();
            await updateNotifier.NotifyTagUpdated(tagId);
        }
    }

    // Applied Tags
    public async Task<long> AddAppliedTagToMediaAsync(long mediaId, long tagId, List<long>? modifierIds,
        long? combinedWithAppliedTagId, string? combineWord)
    {
        var media = await dbContext.MediaFiles.Include(m => m.AppliedTags).FirstOrDefaultAsync(m => m.Id == mediaId) ??
                    throw new ArgumentException("Media not found");

        var appliedTag = new AppliedTag(tagId)
        {
            CombinedWithId = combinedWithAppliedTagId,
            CombineWord = combineWord
        };

        if (modifierIds != null && modifierIds.Count > 0)
        {
            var modifiers = await dbContext.TagModifiers.Where(m => modifierIds.Contains(m.Id)).ToListAsync();
            foreach (var modifier in modifiers)
            {
                appliedTag.Modifiers.Add(modifier);
            }
        }

        media.AppliedTags.Add(appliedTag);
        await SaveAsync();
        await updateNotifier.NotifyMediaUpdated(mediaId);
        return appliedTag.Id;
    }

    public async Task RemoveAppliedTagFromMediaAsync(long mediaId, long appliedTagId)
    {
        var media = await dbContext.MediaFiles.Include(m => m.AppliedTags).FirstOrDefaultAsync(m => m.Id == mediaId) ??
                    throw new ArgumentException("Media not found");

        var appliedTag = media.AppliedTags.FirstOrDefault(t => t.Id == appliedTagId);
        if (appliedTag != null)
        {
            media.AppliedTags.Remove(appliedTag);
            await SaveAsync();
            await updateNotifier.NotifyMediaUpdated(mediaId);
        }
    }

    public async Task<long> AddAppliedTagToCollectionAsync(long collectionId, long tagId, List<long>? modifierIds,
        long? combinedWithAppliedTagId, string? combineWord)
    {
        var collection = await dbContext.Collections.Include(c => c.AppliedTags)
                             .FirstOrDefaultAsync(c => c.Id == collectionId) ??
                         throw new ArgumentException("Collection not found");

        var appliedTag = new AppliedTag(tagId)
        {
            CombinedWithId = combinedWithAppliedTagId,
            CombineWord = combineWord
        };

        if (modifierIds != null && modifierIds.Count > 0)
        {
            var modifiers = await dbContext.TagModifiers.Where(m => modifierIds.Contains(m.Id)).ToListAsync();
            foreach (var modifier in modifiers)
            {
                appliedTag.Modifiers.Add(modifier);
            }
        }

        collection.AppliedTags.Add(appliedTag);
        await SaveAsync();
        await updateNotifier.NotifyCollectionUpdated(collectionId);
        return appliedTag.Id;
    }

    public async Task RemoveAppliedTagFromCollectionAsync(long collectionId, long appliedTagId)
    {
        var collection = await dbContext.Collections.Include(c => c.AppliedTags)
                             .FirstOrDefaultAsync(c => c.Id == collectionId) ??
                         throw new ArgumentException("Collection not found");

        var appliedTag = collection.AppliedTags.FirstOrDefault(t => t.Id == appliedTagId);
        if (appliedTag != null)
        {
            collection.AppliedTags.Remove(appliedTag);
            await SaveAsync();
            await updateNotifier.NotifyCollectionUpdated(collectionId);
        }
    }

    public async Task DeleteOrphanedAppliedTagsAsync()
    {
        var orphanedAppliedTags = await dbContext.AppliedTags
            .IgnoreQueryFilters()
            .Where(appliedTag => !appliedTag.MediaFiles.Any() && !appliedTag.Collections.Any())
            .ToListAsync();

        if (orphanedAppliedTags.Count == 0)
            return;

        dbContext.AppliedTags.RemoveRange(orphanedAppliedTags);
        await SaveAsync();
        logger.LogInformation("Deleted {Count} orphaned applied tags", orphanedAppliedTags.Count);
    }

    // Download Galleries
    public async Task<long> CreateDownloadGalleryAsync(string galleryUrl)
    {
        var gallery = new DownloadGallery(galleryUrl.TrimOrThrowIfEmpty());
        await dbContext.DownloadGalleries.AddAsync(gallery);
        await SaveAsync();
        await updateNotifier.NotifyDownloadGalleriesUpdated();
        return gallery.Id;
    }

    public async Task UpdateDownloadGalleryAsync(long id, string? targetPath, string? galleryName, bool? isDownloaded,
        string? tagsString)
    {
        var gallery = await dbContext.DownloadGalleries.FindAsync(id) ??
                      throw new ArgumentException("Gallery not found");

        if (targetPath != null)
            gallery.TargetPath = targetPath;

        if (galleryName != null)
            gallery.GalleryName = galleryName;

        if (isDownloaded != null)
            gallery.IsDownloaded = isDownloaded.Value;

        if (tagsString != null)
            gallery.TagsString = tagsString;

        gallery.UpdatedAt = DateTime.UtcNow;
        await SaveAsync();
        await updateNotifier.NotifyDownloadGalleryUpdated(id);
    }

    public async Task DeleteDownloadGalleryAsync(long id)
    {
        var gallery = await dbContext.DownloadGalleries.FindAsync(id) ??
                      throw new ArgumentException("Gallery not found");
        gallery.IsDeleted = true;
        gallery.UpdatedAt = DateTime.UtcNow;
        await SaveAsync();
        await updateNotifier.NotifyDownloadGalleriesUpdated();
    }

    // Server-only model variants
    public async Task<List<Tag>> GetTagsAsync()
    {
        return await dbContext.Tags.ToListAsync();
    }

    public async Task<Tag?> GetTagAsync(long id)
    {
        return await dbContext.Tags.FindAsync(id);
    }

    public async Task<Tag?> GetTagByNameAsync(string name)
    {
        name = name.ToLowerInvariant();
        return await dbContext.Tags.FirstOrDefaultAsync(t => t.Name == name);
    }

    public async Task<List<TagModifier>> GetTagModifiersAsync()
    {
        return await dbContext.TagModifiers.ToListAsync();
    }

    public async Task<TagModifier?> GetTagModifierAsync(long id)
    {
        return await dbContext.TagModifiers.FindAsync(id);
    }

    public async Task<TagModifier?> GetTagModifierByNameAsync(string name)
    {
        name = name.ToLowerInvariant();
        return await dbContext.TagModifiers.FirstOrDefaultAsync(m => m.Name == name);
    }

    public async Task<List<AppliedTag>> GetMediaAppliedTagsAsync(long mediaId)
    {
        return await dbContext.AppliedTags
            .Include(t => t.Tag)
            .Include(t => t.Modifiers)
            .Where(t => t.MediaFiles.Any(m => m.Id == mediaId))
            .ToListAsync();
    }

    public async Task<List<AppliedTag>> GetCollectionAppliedTagsAsync(long collectionId)
    {
        return await dbContext.AppliedTags
            .Include(t => t.Tag)
            .Include(t => t.Modifiers)
            .Where(t => t.Collections.Any(c => c.Id == collectionId))
            .ToListAsync();
    }

    public async Task<AppliedTag?> GetAppliedTagAsync(long id)
    {
        return await dbContext.AppliedTags
            .Include(t => t.Tag)
            .Include(t => t.Modifiers)
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task<Tag?> GetTagByNameOrAliasAsync(string name)
    {
        name = name.ToLowerInvariant();
        var tag = await GetTagByNameAsync(name);
        if (tag != null)
            return tag;

        var alias = await dbContext.TagAliases.Include(a => a.Tag).FirstOrDefaultAsync(a => a.Name == name);
        return alias?.Tag;
    }

    public async Task<TagBreakRule?> GetTagBreakRuleByStrAsync(string str)
    {
        // For now, exact match as in C++
        return await dbContext.TagBreakRules.Include(r => r.ActualTag)
            .Include(r => r.Modifiers)
            .FirstOrDefaultAsync(r => r.TagString == str);
    }

    public async Task<string?> GetTagSuperAliasAsync(string alias)
    {
        alias = alias.ToLowerInvariant();
        var superAlias = await dbContext.TagSuperAliases.FindAsync(alias);
        return superAlias?.Expanded;
    }

    public async Task<List<string>> SelectTagNamesWildcardAsync(string pattern, int maxCount = 50)
    {
        pattern = pattern.ToLowerInvariant();
        return await dbContext.Tags
            .Where(t => t.Name.Contains(pattern))
            .OrderBy(t => t.Name)
            .Take(maxCount)
            .Select(t => t.Name)
            .ToListAsync();
    }

    public async Task<List<string>> SelectTagAliasesWildcardAsync(string pattern, int maxCount = 50)
    {
        pattern = pattern.ToLowerInvariant();
        return await dbContext.TagAliases
            .Where(a => a.Name.Contains(pattern))
            .OrderBy(a => a.Name)
            .Take(maxCount)
            .Select(a => a.Name)
            .ToListAsync();
    }

    public async Task<List<string>> SelectTagModifierNamesWildcardAsync(string pattern, int maxCount = 50)
    {
        pattern = pattern.ToLowerInvariant();
        return await dbContext.TagModifiers
            .Where(m => m.Name.Contains(pattern))
            .OrderBy(m => m.Name)
            .Take(maxCount)
            .Select(m => m.Name)
            .ToListAsync();
    }

    public async Task<List<string>> SelectTagBreakRulesByStrWildcardAsync(string pattern, int maxCount = 50)
    {
        return await dbContext.TagBreakRules
            .Where(r => r.TagString.Contains(pattern))
            .OrderBy(r => r.TagString)
            .Take(maxCount)
            .Select(r => r.TagString)
            .ToListAsync();
    }

    public async Task<List<string>> SelectTagSuperAliasWildcardAsync(string pattern, int maxCount = 50)
    {
        pattern = pattern.ToLowerInvariant();
        return await dbContext.TagSuperAliases
            .Where(s => s.Alias.Contains(pattern))
            .OrderBy(s => s.Alias)
            .Take(maxCount)
            .Select(s => s.Alias)
            .ToListAsync();
    }

    public async Task<List<Tag>> SearchTagsWildcardAsync(string search)
    {
        var pattern = search.ToLowerInvariant();
        return await dbContext.Tags
            .Where(t => t.Name.Contains(pattern))
            .OrderBy(t => t.Name)
            .Take(100)
            .ToListAsync();
    }

    async Task<List<TagDTO>> IClientDatabaseService.SearchTagsWildcardAsync(string search)
    {
        return (await SearchTagsWildcardAsync(search)).Select(t => t.GetDTO()).ToList();
    }

    async Task<TagDTO?> IClientDatabaseService.GetTagByNameAsync(string name)
    {
        var tag = await GetTagByNameOrAliasAsync(name);
        return tag?.GetDTO();
    }

    public async Task<MediaImportInfo?> GetMediaImportInfoAsync(long mediaId)
    {
        return await dbContext.MediaImportInfos.FirstOrDefaultAsync(i => i.MediaFileId == mediaId);
    }

    public async Task SaveMediaImportInfoAsync(MediaImportInfo importInfo)
    {
        if (dbContext.Entry(importInfo).State == EntityState.Detached)
            await dbContext.MediaImportInfos.AddAsync(importInfo);

        await SaveAsync();
    }

    public async Task<List<DownloadGallery>> GetDownloadGalleriesAsync()
    {
        return await dbContext.DownloadGalleries.ToListAsync();
    }

    public async Task<DownloadGallery?> GetDownloadGalleryAsync(long id)
    {
        return await dbContext.DownloadGalleries.FindAsync(id);
    }

    public async Task<DownloadGallery?> GetDownloadGalleryByUrlAsync(string url)
    {
        return await dbContext.DownloadGalleries.FirstOrDefaultAsync(g => g.GalleryUrl == url);
    }

    public async Task AddIgnoredDuplicateAsync(long mediaId1, long mediaId2)
    {
        if (mediaId1 == mediaId2)
            return;

        // Normalize order to avoid duplicates in different order
        long first = Math.Min(mediaId1, mediaId2);
        long second = Math.Max(mediaId1, mediaId2);

        var alreadyExists = await dbContext.IgnoredDuplicates.AnyAsync(id =>
            id.MediaFileId1 == first && id.MediaFileId2 == second);

        if (alreadyExists)
            return;

        var ignored = new IgnoredDuplicate(first, second);
        await dbContext.IgnoredDuplicates.AddAsync(ignored);
        await SaveAsync();
    }

    public async Task RemoveIgnoredDuplicateAsync(long mediaId1, long mediaId2)
    {
        long first = Math.Min(mediaId1, mediaId2);
        long second = Math.Max(mediaId1, mediaId2);

        var ignored = await dbContext.IgnoredDuplicates.FindAsync(first, second);
        if (ignored != null)
        {
            dbContext.IgnoredDuplicates.Remove(ignored);
            await SaveAsync();
        }
    }

    public async Task<bool> IsIgnoredDuplicateAsync(long mediaId1, long mediaId2)
    {
        long first = Math.Min(mediaId1, mediaId2);
        long second = Math.Max(mediaId1, mediaId2);

        return await dbContext.IgnoredDuplicates.AnyAsync(id => id.MediaFileId1 == first && id.MediaFileId2 == second);
    }

    // Client-side DTO variants
    async Task<List<TagDTO>> IClientDatabaseService.GetAllTagsAsync()
    {
        return (await GetTagsAsync()).Select(t => t.GetDTO()).ToList();
    }

    async Task<TagDTO?> IClientDatabaseService.GetTagAsync(long id)
    {
        return (await GetTagAsync(id))?.GetDTO();
    }

    async Task<List<TagModifierDTO>> IClientDatabaseService.GetAllTagModifiersAsync()
    {
        return (await GetTagModifiersAsync()).Select(m => m.GetDTO()).ToList();
    }

    async Task<TagModifierDTO?> IClientDatabaseService.GetTagModifierAsync(long id)
    {
        return (await GetTagModifierAsync(id))?.GetDTO();
    }

    async Task<List<AppliedTagDTO>> IClientDatabaseService.GetMediaAppliedTagsAsync(long mediaId)
    {
        return (await GetMediaAppliedTagsAsync(mediaId)).Select(t => t.GetDTO()).ToList();
    }

    async Task<List<AppliedTagDTO>> IClientDatabaseService.GetCollectionAppliedTagsAsync(long collectionId)
    {
        return (await GetCollectionAppliedTagsAsync(collectionId)).Select(t => t.GetDTO()).ToList();
    }

    async Task<MediaImportInfoDTO?> IClientDatabaseService.GetMediaImportInfoAsync(long mediaId)
    {
        return (await GetMediaImportInfoAsync(mediaId))?.GetDTO();
    }

    async Task<List<DownloadGalleryDTO>> IClientDatabaseService.GetAllDownloadGalleriesAsync()
    {
        return (await GetDownloadGalleriesAsync()).Select(g => g.GetDTO()).ToList();
    }

    async Task<DownloadGalleryDTO?> IClientDatabaseService.GetDownloadGalleryAsync(long id)
    {
        return (await GetDownloadGalleryAsync(id))?.GetDTO();
    }

    // Note: these are dummy compatibility methods that can be deleted when not found useful when converting
    async Task<ConfiguredMediaDTO?> IClientDatabaseService.GetConfiguredMediaAsync(long mediaConfigId)
    {
        var media = await GetMediaByIdAsync(mediaConfigId);
        return media != null ? new ConfiguredMediaDTO(media.GetDTO()) : null;
    }

    async Task<CollectionDTO?> IClientDatabaseService.GetCollectionAsync(long collectionId)
    {
        return (await GetCollectionAsync(collectionId))?.GetDTO();
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

    public async Task<Tuple<List<ConfiguredMediaInfo>, int>> GetMediaFolderContents(long folderId, int itemPage,
        int pageSize, FolderSortColumn sortColumn, SortDirection sortDirection, string? searchText = null)
    {
        // Fetch subfolders and collections
        var subfolderQuery = dbContext.MediaFolders
            .Where(f => f.Parents.Any(p => p.Id == folderId) && !f.IsDeleted);

        var collectionQuery = dbContext.Collections
            .Where(c => c.Folders.Any(f => f.Id == folderId) && !c.IsDeleted);

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            searchText = searchText.ToLowerInvariant();
            subfolderQuery = subfolderQuery.Where(f => f.NameLowerCase.Contains(searchText));
            collectionQuery = collectionQuery.Where(c => c.NameLowerCase.Contains(searchText));
        }

        // Use lower name to make case-independent sorting
        var subfolders = await subfolderQuery.OrderBy(f => f.NameLowerCase).ToListAsync();
        var collections = await collectionQuery.OrderBy(c => c.NameLowerCase).ToListAsync();

        var allItems = new List<ConfiguredMediaInfo>();

        foreach (var folder in subfolders)
        {
            allItems.Add(new ConfiguredMediaInfo(folder.Name, folder.Id, 0, MediaType.Png, 512, 512)
            {
                IsFolder = true,
            });
        }

        foreach (var collection in collections)
        {
            // Collections have thumbnails, but the client queries them separately
            allItems.Add(new ConfiguredMediaInfo(collection.Name, collection.Id, 0, MediaType.Png, 512, 512)
            {
                IsCollection = true,
            });
        }

        // TODO: handle paging properly for combined results (it seems correct? going further pages goes past the folders)
        int totalItems = allItems.Count;
        var pagedItems = allItems.Skip(itemPage * pageSize).Take(pageSize).ToList();

        return new Tuple<List<ConfiguredMediaInfo>, int>(pagedItems, (int)Math.Ceiling(totalItems / (double)pageSize));
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
