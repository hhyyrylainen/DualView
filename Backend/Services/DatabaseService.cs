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
        logger.LogInformation("Updated application settings");
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
        logger.LogInformation(
            "Created media folder '{FolderName}' ({FolderId}) with parent '{ParentName}' ({ParentId})",
            folder.Name, folder.Id, parent.Name, parent.Id);

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

        var oldName = folder.Name;

        var lowerName = trimmedName.ToLowerInvariant();
        if (folder.Parents.Any(parent => dbContext.MediaFolders.Any(other =>
                other.Id != folderId && other.NameLowerCase == lowerName &&
                other.Parents.Any(parentFolder => parentFolder.Id == parent.Id))))
        {
            throw new Exception("A folder with that name already exists here");
        }

        folder.Name = trimmedName;
        folder.NameLowerCase = lowerName;
        await SaveAsync();
        await updateNotifier.NotifyMediaFoldersUpdated();
        logger.LogInformation("Renamed media folder '{OldName}' ({OldFolderId}) to '{FolderName}' ({FolderId})",
            oldName,
            folderId, folder.Name, folder.Id);
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
        logger.LogInformation(
            "Created collection '{CollectionName}' ({CollectionId}) in folder '{FolderName}' ({FolderId})",
            collection.Name, collection.Id, folder.Name, folder.Id);
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

        var oldName = collection.Name;

        collection.Name = trimmedName;
        collection.NameLowerCase = lowerName;
        await SaveAsync();
        await updateNotifier.NotifyCollectionUpdated(collectionId);
        logger.LogInformation(
            "Renamed collection '{OldName}' ({OldCollectionId}) to '{CollectionName}' ({CollectionId})",
            oldName, collectionId, collection.Name, collection.Id);
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
                logger.LogInformation("Moved collection '{CollectionName}' ({CollectionId}) out of the root folder",
                    collection.Name, collection.Id);
            }

            return;
        }

        collection.Folders.Add(folder);
        await SaveAsync();
        await updateNotifier.NotifyMediaFolderContentsUpdated(folderId);
        if (removedFromRoot)
            await updateNotifier.NotifyMediaFolderContentsUpdated(MediaFolder.RootFolderId);
        logger.LogInformation(
            "Added collection '{CollectionName}' ({CollectionId}) to folder {FolderName} ({FolderId})",
            collection.Name, collection.Id, folder.Name, folder.Id);
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

        if (await dbContext.MediaFolders.AnyAsync(existing =>
                existing.Id != folderId &&
                existing.NameLowerCase == folder.NameLowerCase &&
                existing.Parents.Any(existingParent => existingParent.Id == parentFolderId)))
        {
            throw new InvalidOperationException("A folder with that name already exists in the parent folder");
        }

        folder.Parents.Add(parent);
        await SaveAsync();
        await updateNotifier.NotifyMediaFoldersUpdated();
        logger.LogInformation(
            "Added folder '{FolderName}' ({FolderId}) to parent folder '{ParentFolderName}' ({ParentFolderId})",
            folder.Name, folder.Id, parent.Name, parent.Id);
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
        logger.LogInformation(
            "Removed collection '{CollectionName}' ({CollectionId}) from folder '{FolderName}' ({FolderId})",
            collection.Name, collection.Id, folder.Name, folder.Id);
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
        logger.LogInformation(
            "Removed folder '{FolderName}' ({FolderId}) from parent folder '{ParentFolderName}' ({ParentFolderId})",
            folder.Name, folder.Id, parent.Name, parent.Id);
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

        List<CollectionItem> uncategorizedItems = new();
        var temporaryMediaIds = new List<long>();
        if (collectionId != Collection.UncategorizedCollectionId)
        {
            foreach (var mediaFile in mediaFiles.Values)
            {
                if (mediaFile.IsTemporary)
                {
                    mediaFile.IsTemporary = false;
                    temporaryMediaIds.Add(mediaFile.Id);
                }
            }

            uncategorizedItems = await dbContext.Set<CollectionItem>()
                .Where(item => item.CollectionId == Collection.UncategorizedCollectionId &&
                               newMediaIds.Contains(item.MediaFileId))
                .ToListAsync();
            dbContext.Set<CollectionItem>().RemoveRange(uncategorizedItems);
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
        if (uncategorizedItems.Count > 0)
            await updateNotifier.NotifyCollectionContentsUpdated(Collection.UncategorizedCollectionId);
        foreach (var mediaId in temporaryMediaIds)
            await updateNotifier.NotifyMediaUpdated(mediaId);
        logger.LogInformation("Added {MediaCount} media items to collection '{CollectionName}' ({CollectionId})",
            newMediaIds.Count, collection.Name, collection.Id);
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
        logger.LogInformation("Set collection '{CollectionName}' ({CollectionId}) image group size to {ImageGroupSize}",
            collection.Name, collection.Id, imageGroupSize);
    }

    public async Task ReorderCollection(long collectionId, List<long> newImageOrderIds)
    {
        var collection = await dbContext.Collections
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.Id == collectionId);

        if (collection == null)
            throw new ArgumentException("Collection not found");

        var itemsByMediaId = collection.Items.ToDictionary(ci => ci.MediaFileId);

        // SQLite enforces the unique (collection, sequence) index during each update,
        // so clear the old positions before assigning the new order.
        var temporarySequence = -1;
        foreach (var item in collection.Items)
        {
            item.SequenceNumber = temporarySequence--;
        }

        await SaveAsync();

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
        logger.LogInformation("Reordered {ItemCount} items in collection '{CollectionName}' ({CollectionId})",
            newImageOrderIds.Count, collection.Name, collection.Id);
    }

    public async Task RemoveMediaFromCollection(long mediaId, long collectionId)
    {
        var item = await dbContext.Set<CollectionItem>().FirstOrDefaultAsync(ci =>
            ci.CollectionId == collectionId && ci.MediaFileId == mediaId);

        if (item == null)
            return;

        await ValidateCollectionRemovalAsync(collectionId, [mediaId]);
        dbContext.Set<CollectionItem>().Remove(item);
        await SaveAsync();
        await updateNotifier.NotifyCollectionContentsUpdated(collectionId);
        logger.LogInformation("Removed media {MediaId} from collection {CollectionId}", mediaId, collectionId);
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
        var collection = await dbContext.Collections.FindAsync(collectionId) ??
                         throw new ArgumentException("Collection not found");
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

        await ValidateCollectionRemovalAsync(collectionId, selectedIds);
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
        logger.LogInformation("Removed {ItemCount} media items from collection '{CollectionName}' ({CollectionId})",
            items.Count, collection.Name, collection.Id);
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
        logger.LogInformation("Restored {ItemCount} items in collection '{CollectionName}' ({CollectionId})",
            itemsToRestore.Count, collection.Name, collection.Id);
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
        logger.LogInformation("Created media folder '{FolderName}' ({FolderId}) with parent folder ID {ParentFolderId}",
            folder.Name, folder.Id, parentId);
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
        logger.LogInformation("Deleted media file {MediaId}", mediaId);
    }

    public async Task RestoreMediaAsync(long mediaId)
    {
        var media = await dbContext.MediaFiles
                        .IgnoreQueryFilters()
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
        logger.LogInformation("Purged media file {MediaId}", mediaId);
    }

    public async Task<List<MediaFile>> GetDeletedMediaAsync(int limit)
    {
        return await dbContext.MediaFiles
            .IgnoreQueryFilters()
            .Where(m => m.IsDeleted)
            .OrderByDescending(m => m.UpdatedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<MediaFolder>> GetDeletedMediaFoldersAsync(int limit)
    {
        return await dbContext.MediaFolders
            .IgnoreQueryFilters()
            .Where(f => f.IsDeleted)
            .OrderByDescending(f => f.UpdatedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<Collection>> GetDeletedCollectionsAsync(int limit)
    {
        return await dbContext.Collections
            .IgnoreQueryFilters()
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
        logger.LogInformation("Updated media file {MediaId}", media.Id);
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

    public async Task<List<string>> SearchUploadTargetNamesAsync(string search, int limit = 100)
    {
        var searchLower = search.Trim().ToLowerInvariant();
        limit = Math.Clamp(limit, 1, 200);

        var sectionNames = dbContext.UploadSections
            .Where(section => section.NameLowercase.Contains(searchLower))
            .Select(section => section.Name);
        var collectionNames = dbContext.Collections
            .Where(collection => collection.NameLowerCase.Contains(searchLower))
            .Select(collection => collection.Name);

        // TODO: investigate if this builds a single DB query or not
        return await sectionNames.Concat(collectionNames)
            .Distinct()
            .OrderBy(name => name.ToLower().StartsWith(searchLower) ? 0 : 1)
            .ThenBy(name => name.ToLower().IndexOf(searchLower))
            .ThenBy(name => name)
            .Take(limit)
            .ToListAsync();
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

    public async Task<CollectionBrowseInfoDTO> GetCollectionBrowseInfoAsync(long collectionId, long? mediaId = null)
    {
        var query = dbContext.Set<CollectionItem>()
            .Where(item => item.CollectionId == collectionId);
        var count = await query.CountAsync();
        int? index = null;

        if (mediaId.HasValue)
        {
            var currentItem = await query
                .Where(item => item.MediaFileId == mediaId.Value)
                .Select(item => new
                {
                    item.SequenceNumber,
                    item.MediaFileId,
                })
                .FirstOrDefaultAsync();
            if (currentItem != null)
            {
                // Count actual rows before this item. Sequence numbers can have gaps after deletions.
                index = await query.CountAsync(item =>
                    item.SequenceNumber < currentItem.SequenceNumber ||
                    item.SequenceNumber == currentItem.SequenceNumber && item.MediaFileId < currentItem.MediaFileId);
            }
        }

        return new CollectionBrowseInfoDTO
        {
            Count = count,
            Index = index,
        };
    }

    public async Task<MediaFileDTO?> GetCollectionMediaAtIndexAsync(long collectionId, int index)
    {
        if (index < 0)
            return null;

        return await dbContext.Set<CollectionItem>()
            .Where(item => item.CollectionId == collectionId)
            .OrderBy(item => item.SequenceNumber)
            .ThenBy(item => item.MediaFileId)
            .Skip(index)
            .Select(item => item.MediaFile.GetDTO())
            .FirstOrDefaultAsync();
    }

    async Task<List<MediaFileDTO>> IClientDatabaseService.GetCollectionContents(long collectionId)
    {
        return (await GetCollectionContents(collectionId)).ConvertToDTO<MediaFile, MediaFileDTO>();
    }

    Task<CollectionBrowseInfoDTO> IClientDatabaseService.GetCollectionBrowseInfoAsync(long collectionId,
        long? mediaId) => GetCollectionBrowseInfoAsync(collectionId, mediaId);

    Task<MediaFileDTO?> IClientDatabaseService.GetCollectionMediaAtIndexAsync(long collectionId, int index) =>
        GetCollectionMediaAtIndexAsync(collectionId, index);

    public async Task<List<Collection>> GetCollectionsInFolderAsync(long folderId)
    {
        return await dbContext.Collections.Where(c => c.Folders.Any(f => f.Id == folderId)).ToListAsync();
    }

    public async Task<Collection?> GetCollectionByNameAsync(string name)
    {
        var lowercaseName = name.Trim().ToLowerInvariant();
        return await dbContext.Collections.FirstOrDefaultAsync(c => c.NameLowerCase == lowercaseName);
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
        logger.LogInformation("Updated collection '{CollectionName}' ({CollectionId})", collection.Name,
            collection.Id);
    }

    public async Task DeleteMediaFolderAsync(long folderId)
    {
        var folder = await dbContext.MediaFolders.FindAsync(folderId) ??
                     throw new ArgumentException("Folder not found");
        folder.IsDeleted = true;
        folder.UpdatedAt = DateTime.UtcNow;
        await SaveAsync();
        await updateNotifier.NotifyMediaFoldersUpdated();
        logger.LogInformation("Deleted media folder '{FolderName}' ({FolderId})", folder.Name, folder.Id);
    }

    public async Task RestoreMediaFolderAsync(long folderId)
    {
        var folder = await dbContext.MediaFolders.FindAsync(folderId) ??
                     throw new ArgumentException("Folder not found");
        folder.IsDeleted = false;
        folder.UpdatedAt = DateTime.UtcNow;
        await SaveAsync();
        await updateNotifier.NotifyMediaFoldersUpdated();
        logger.LogInformation("Restored media folder '{FolderName}' ({FolderId})", folder.Name, folder.Id);
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
        logger.LogInformation("Purged media folder '{FolderName}' ({FolderId})", folder.Name, folder.Id);
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

        logger.LogInformation("Deleted collection '{CollectionName}' ({CollectionId})", collection.Name,
            collection.Id);
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

        logger.LogInformation("Deleted collection '{CollectionName}' ({CollectionId}) and {MediaCount} media files",
            collection.Name, collection.Id, mediaFiles.Count);

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

        logger.LogInformation("Restored collection '{CollectionName}' ({CollectionId})", collection.Name,
            collection.Id);
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

        logger.LogInformation(
            "Purged collection '{CollectionName}' ({CollectionId}) and re-categorized {MediaCount} media files",
            collection.Name, collection.Id, mediaToCategorize.Count);
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
            .Where(m => !m.InCollections.Any() && !m.InUploadSections.Any())
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
            .OrderByDescending(ci => ci.SequenceNumber)
            .Select(ci => ci.SequenceNumber)
            .FirstOrDefaultAsync() + 1;
    }

    public async Task SaveMediaFileAsync(MediaFile mediaFile)
    {
        await SaveAsync();
        await updateNotifier.NotifyMediaUpdated(mediaFile.Id);
        logger.LogInformation("Updated media file {MediaId}", mediaFile.Id);
    }

    public async Task<UploadSection> GetOrCreateUploadSectionAsync(string? sectionName)
    {
        if (string.IsNullOrWhiteSpace(sectionName))
        {
            var selected = await dbContext.UploadSections.FirstOrDefaultAsync(s => s.Selected);
            if (selected != null)
                return selected;

            // An empty section name means "the active section". If there is no active section,
            // always create a new section so media sent after unselecting an import cannot be
            // mixed into an older unnamed section.
            sectionName = "";
        }
        else
        {
            sectionName = sectionName.Trim();
            var lowercase = sectionName.ToLowerInvariant();
            var existing = await dbContext.UploadSections.FirstOrDefaultAsync(s => s.NameLowercase == lowercase);

            if (existing != null)
                return existing;
        }

        // Insert at the start
        var minIndex = await dbContext.UploadSections
            .OrderBy(s => s.DisplayIndex)
            .Select(s => s.DisplayIndex)
            .FirstOrDefaultAsync();

        var newSection = new UploadSection(sectionName)
        {
            DisplayIndex = minIndex - 1,

            // And auto-select if nothing is selected
            Selected = !await dbContext.UploadSections.AnyAsync(s => s.Selected),
        };

        await dbContext.UploadSections.AddAsync(newSection);
        await SaveAsync();
        await updateNotifier.NotifyUploadSectionsUpdated();
        logger.LogInformation("Created upload section '{SectionName}' ({SectionId})", newSection.Name,
            newSection.Id);

        return newSection;
    }

    public async Task<List<UploadSection>> GetUploadSectionsAsync()
    {
        return await dbContext.UploadSections
            .Include(section => section.Items)
            .ThenInclude(item => item.MediaFile)
            .Include(section => section.AppliedTags)
            .ThenInclude(tag => tag.Tag)
            .Include(section => section.AppliedTags)
            .ThenInclude(tag => tag.Modifiers)
            .Include(section => section.AppliedTags)
            .ThenInclude(tag => tag.CombinedWith)
            .ThenInclude(tag => tag!.Tag)
            .OrderBy(section => section.DisplayIndex)
            .AsSplitQuery()
            .ToListAsync();
    }

    public async Task<List<RecentImportSectionDTO>> GetRecentImportSectionsAsync()
    {
        var activeNames = await dbContext.UploadSections
            .OrderBy(section => section.DisplayIndex)
            .Select(section => section.Name)
            .ToListAsync();
        var historicalSections = await dbContext.RecentImportSections
            .OrderByDescending(section => section.LastUsed)
            .ToListAsync();

        var result = new List<RecentImportSectionDTO>(activeNames.Count + historicalSections.Count);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in activeNames)
        {
            if (string.IsNullOrWhiteSpace(name) || !names.Add(name))
                continue;

            result.Add(new RecentImportSectionDTO
            {
                Name = name,
                IsActive = true,
            });
        }

        foreach (var section in historicalSections)
        {
            if (!names.Add(section.Name))
                continue;

            result.Add(new RecentImportSectionDTO
            {
                Name = section.Name,
                LastUsed = section.LastUsed,
            });
        }

        return result;
    }

    public async Task DeleteUploadSectionAsync(long sectionId)
    {
        var section = await dbContext.UploadSections.FindAsync(sectionId)
                      ?? throw new ArgumentException("Section not found");
        var wasActive = section.Selected;

        dbContext.UploadSections.Remove(section);
        await SaveAsync();

        if (wasActive)
            await updateNotifier.NotifyUploadSectionActiveChanged(null);
        await updateNotifier.NotifyUploadSectionsUpdated();
        logger.LogInformation("Deleted upload section '{Name}' ({SectionId})", section.Name, sectionId);
    }

    public async Task<UploadSection?> GetUploadSectionAsync(long sectionId)
    {
        return await dbContext.UploadSections
            .Include(section => section.Items)
            .ThenInclude(item => item.MediaFile)
            .Include(section => section.AppliedTags)
            .ThenInclude(tag => tag.Tag)
            .Include(section => section.AppliedTags)
            .ThenInclude(tag => tag.Modifiers)
            .Include(section => section.AppliedTags)
            .ThenInclude(tag => tag.CombinedWith)
            .ThenInclude(tag => tag!.Tag)
            .AsSplitQuery()
            .FirstOrDefaultAsync(section => section.Id == sectionId);
    }

    public async Task SaveUploadSectionAsync(UploadSection section)
    {
        section.Name = section.Name.Trim();
        var targetCollection = await GetCollectionByNameAsync(section.Name);
        if (targetCollection != null)
            section.Name = targetCollection.Name;
        section.UpdatedAt = DateTime.UtcNow;

        await SaveAsync();
        await updateNotifier.NotifyUploadSectionUpdated(section.Id);
        logger.LogDebug("Updated upload section '{SectionName}' ({SectionId})", section.Name, section.Id);
    }

    public async Task RemoveMediaFromUploadSectionAsync(long sectionId, List<long> mediaIds)
    {
        var items = await dbContext.UploadSectionItems
            .Where(item => item.UploadSectionId == sectionId && mediaIds.Contains(item.MediaFileId))
            .ToListAsync();
        dbContext.UploadSectionItems.RemoveRange(items);

        await SaveAsync();
        await updateNotifier.NotifyUploadSectionContentsUpdated(sectionId);
        logger.LogInformation("Removed {ItemCount} items from upload section {SectionId}", items.Count, sectionId);
    }

    public async Task ReorderUploadSectionAsync(long sectionId, List<long> mediaIds)
    {
        var items = await dbContext.UploadSectionItems
            .Where(item => item.UploadSectionId == sectionId)
            .ToListAsync();
        var itemsById = items.ToDictionary(item => item.MediaFileId);

        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        // Move every item to a temporary range first so the unique index constraint cannot collide
        // while the final order is being applied. Keep the original relative order for leftovers.
        foreach (var item in items)
            item.Index += items.Count;
        await SaveAsync();

        var nextIndex = 0;
        foreach (var mediaId in mediaIds.Distinct())
        {
            if (itemsById.Remove(mediaId, out var item))
                item.Index = nextIndex++;
        }

        foreach (var item in itemsById.Values.OrderBy(item => item.Index))
            item.Index = nextIndex++;

        await SaveAsync();
        await transaction.CommitAsync();
        await updateNotifier.NotifyUploadSectionContentsUpdated(sectionId);
        logger.LogInformation("Reordered {ItemCount} items in upload section {SectionId}", items.Count, sectionId);
    }

    public async Task SetUploadSectionActiveAsync(long? sectionId)
    {
        var sections = await dbContext.UploadSections.ToListAsync();
        var previousActiveSection = sections.FirstOrDefault(section => section.Selected);
        foreach (var section in sections)
            section.Selected = sectionId.HasValue && section.Id == sectionId.Value;
        await SaveAsync();

        var activeSection = sections.FirstOrDefault(section => section.Selected);
        if (previousActiveSection?.Id != activeSection?.Id)
        {
            await updateNotifier.NotifyUploadSectionActiveChanged(activeSection?.Id);
            logger.LogInformation(
                "Changed active upload section from '{PreviousSectionName}' ({PreviousSectionId}) to " +
                "'{SectionName}' ({SectionId})", previousActiveSection?.Name ?? "<none>",
                previousActiveSection?.Id, activeSection?.Name ?? "<none>", activeSection?.Id);
        }
    }

    public async Task ImportUploadSectionAsync(long sectionId, List<long>? mediaIds)
    {
        var section = await dbContext.UploadSections
            .Include(item => item.Items)
            .Include(item => item.AppliedTags)
            .ThenInclude(tag => tag.Modifiers)
            .Include(item => item.AppliedTags)
            .ThenInclude(tag => tag.Tag)
            .Include(item => item.AppliedTags)
            .ThenInclude(tag => tag.CombinedWith)
            .ThenInclude(tag => tag!.Tag)
            .AsSplitQuery()
            .FirstOrDefaultAsync(item => item.Id == sectionId) ?? throw new ArgumentException("Section not found");
        var selectedIds = section.Items.OrderBy(item => item.Index)
            .Select(item => item.MediaFileId)
            .Where(id => mediaIds == null || mediaIds.Contains(id))
            .Distinct()
            .ToList();
        if (selectedIds.Count == 0)
            return;

        logger.LogInformation("Trying to import {ItemCount} items from upload section '{SectionName}' ({SectionId})",
            selectedIds.Count, section.Name, section.Id);

        var originalSectionName = section.Name;
        var targetCollectionName = section.Name.Trim();
        var targetFolderApplied = section.TargetFolderId != MediaFolder.RootFolderId;

        // Disallow creating collections with no name
        if (string.IsNullOrWhiteSpace(targetCollectionName))
            throw new ArgumentException("Section name cannot be empty or whitespace");

        var collection = await GetCollectionByNameAsync(targetCollectionName);
        if (collection == null)
        {
            // This will throw if the name is empty or whitespace
            collection = new Collection(targetCollectionName);
            await dbContext.Collections.AddAsync(collection);
            await SaveAsync();
            await AddCollectionToFolder(collection.Id, section.TargetFolderId);
        }
        else
        {
            section.Name = collection.Name;

            // Collections are globally unique, but a collection can be linked to multiple folders.
            // Ensure an existing collection is linked to the selected non-root target folder too.
            if (section.TargetFolderId != MediaFolder.RootFolderId)
                await AddCollectionToFolder(collection.Id, section.TargetFolderId);
        }

        await AddRecentImportSectionAsync(section.Name);

        var nextSequence = await GetNextCollectionSequenceNumberAsync(collection.Id);
        await AddMediaToCollection(selectedIds, collection.Id, nextSequence);

        await dbContext.Entry(collection).Collection(item => item.AppliedTags).LoadAsync();
        var hadSectionTags = section.AppliedTags.Count > 0;
        foreach (var sectionTag in section.AppliedTags)
        {
            var storedTag = await GetOrCreateAppliedTagAsync(sectionTag.GetDTO());
            if (collection.AppliedTags.Any(collectionTag => collectionTag.Id == storedTag.Id))
                continue;

            collection.AppliedTags.Add(storedTag);

            // A bit of expensive call, but we want a full record of the applied tags
            logger.LogInformation("Applying new tag ({Name}) on import to collection '{CollectionName}'",
                AppliedTagText.ToText(storedTag.GetDTO()), collection.Name);
        }

        // Clear these to let the user keep importing stuff without accidentally putting tags all over
        section.AppliedTags.Clear();

        // Need to remove items first to detect when things become blank
        var itemsRemoved = 0;
        if (section.RemoveAfterImport)
        {
            var items = section.Items.Where(item => selectedIds.Contains(item.MediaFileId)).ToList();
            itemsRemoved = items.Count;
            dbContext.UploadSectionItems.RemoveRange(items);
        }

        // The removed items are still present in the database until SaveAsync is called, so checking
        // the database here would incorrectly keep a section that just had its last items imported.
        var hasRemainingItems = section.Items.Any(item => !selectedIds.Contains(item.MediaFileId));
        if (!section.KeepTarget && section.RemoveAfterImport && !hasRemainingItems)
        {
            dbContext.UploadSections.Remove(section);
            await SaveAsync();
            await updateNotifier.NotifyUploadSectionsUpdated();
            logger.LogInformation("Removed empty upload section '{SectionName}' ({SectionId}) after import",
                section.Name, section.Id);
            return;
        }

        if (targetFolderApplied)
            section.TargetFolderId = MediaFolder.RootFolderId;

        var sectionUpdated = hadSectionTags || targetFolderApplied ||
                             !string.Equals(originalSectionName, section.Name, StringComparison.Ordinal);

        if (section.RemoveAfterImport)
        {
            await SaveAsync();

            if (sectionUpdated)
                await updateNotifier.NotifyUploadSectionUpdated(sectionId);

            // Only notify if we still existed
            await updateNotifier.NotifyUploadSectionContentsUpdated(sectionId);
            logger.LogInformation("Removed {ItemCount} imported items from upload section {SectionId}", itemsRemoved,
                sectionId);
        }
        else if (sectionUpdated)
        {
            await SaveAsync();
            await updateNotifier.NotifyUploadSectionUpdated(sectionId);
        }
    }

    public async Task AddMediaToUploadSectionAsync(long mediaId, long sectionId, int index)
    {
        if (await dbContext.UploadSectionItems.AnyAsync(item => item.MediaFileId == mediaId &&
                                                                item.UploadSectionId == sectionId))
        {
            return;
        }

        if (await dbContext.UploadSectionItems.AnyAsync(item => item.UploadSectionId == sectionId &&
                                                                item.Index == index))
        {
            index = await GetNextUploadSectionIndexAsync(sectionId);
        }

        var item = new UploadSectionItem
        {
            UploadSectionId = sectionId,
            MediaFileId = mediaId,
            Index = index,
        };

        await dbContext.UploadSectionItems.AddAsync(item);

        await SaveAsync();
        await updateNotifier.NotifyUploadSectionContentsUpdated(sectionId);
        logger.LogInformation("Added media {MediaId} to upload section {SectionId}", mediaId, sectionId);
    }

    public async Task AddMediaToActiveUploadSectionAsync(List<long> mediaIds)
    {
        var selectedMediaIds = mediaIds.Distinct().ToList();
        if (selectedMediaIds.Count == 0)
            return;

        var section = await GetOrCreateUploadSectionAsync(null);
        var existingMediaIds = await dbContext.UploadSectionItems
            .Where(item => item.UploadSectionId == section.Id && selectedMediaIds.Contains(item.MediaFileId))
            .Select(item => item.MediaFileId)
            .ToListAsync();
        var mediaIdsToAdd = selectedMediaIds.Except(existingMediaIds).ToList();
        if (mediaIdsToAdd.Count == 0)
            return;

        var nextIndex = await GetNextUploadSectionIndexAsync(section.Id);
        foreach (var mediaId in mediaIdsToAdd)
        {
            await dbContext.UploadSectionItems.AddAsync(new UploadSectionItem
            {
                UploadSectionId = section.Id,
                MediaFileId = mediaId,
                Index = nextIndex++,
            });
        }

        await SaveAsync();
        await updateNotifier.NotifyUploadSectionContentsUpdated(section.Id);
        logger.LogInformation("Added {MediaCount} media items to active upload section {SectionId}",
            mediaIdsToAdd.Count, section.Id);
    }

    public async Task<int> GetNextUploadSectionIndexAsync(long sectionId)
    {
        return await dbContext.UploadSectionItems
            .Where(i => i.UploadSectionId == sectionId)
            .OrderByDescending(i => i.Index)
            .Select(i => i.Index)
            .FirstOrDefaultAsync() + 1;
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
        logger.LogInformation("Set media {MediaId} temporary status to {IsTemporary}", mediaId, isTemporary);
    }

    public async Task BumpUploadSectionLastImportedAsync(long sectionId)
    {
        var section = await dbContext.UploadSections.FindAsync(sectionId) ??
                      throw new ArgumentException("Section not found");
        section.LastImported = DateTime.UtcNow;
        section.BumpUpdatedAtTime();
        await SaveAsync();

        logger.LogInformation("Updated last imported time for upload section '{SectionName}' ({SectionId})",
            section.Name, section.Id);

        // For now, this is not shown in the GUI, so we don't need to trigger an update event
        // await updateNotifier.NotifyUploadSectionUpdated(sectionId);
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

        logger.LogInformation("Created media file {MediaId} in collection '{CollectionName}' ({CollectionId})",
            mediaItem.Id, collection.Name, collection.Id);

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

        logger.LogInformation("Created media file {MediaId} in upload section '{SectionName}' ({SectionId})",
            mediaItem.Id, section.Name, section.Id);

        return mediaItem;
    }

    public async Task<List<MediaFile>> GetEligibleMediaFilesForPurgeAsync(TimeSpan timeSinceDeletion)
    {
        var cutoffTime = DateTime.UtcNow - timeSinceDeletion;

        return await dbContext.MediaFiles
            .Where(m => m.IsDeleted && m.UpdatedAt <= cutoffTime)
            .ToListAsync();
    }

    public async Task<List<MediaFile>> GetEligibleTemporaryMediaFilesForPurgeAsync(TimeSpan timeSinceUpdate)
    {
        var cutoffTime = DateTime.UtcNow - timeSinceUpdate;

        return await dbContext.MediaFiles
            .IgnoreQueryFilters()
            .Where(media => media.IsTemporary && media.UpdatedAt <= cutoffTime)
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
        logger.LogInformation("Created maintenance record '{RecordName}'", record.Name);
    }

    public Task SaveMaintenanceRecord(MaintenanceJobRecord record)
    {
        return SaveAsync();
    }

    public Task DeleteMaintenanceRecord(MaintenanceJobRecord record)
    {
        dbContext.MaintenanceJobRecords.Remove(record);
        logger.LogInformation("Deleted maintenance record '{RecordName}'", record.Name);
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
        logger.LogInformation("Created tag {TagId} '{TagName}'", tag.Id, tag.Name);
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
        logger.LogInformation("Updated tag {TagId}", id);
    }

    public async Task DeleteTagAsync(long id)
    {
        var tag = await dbContext.Tags.FindAsync(id) ?? throw new ArgumentException("Tag not found");
        tag.IsDeleted = true;
        tag.UpdatedAt = DateTime.UtcNow;
        await SaveAsync();
        await updateNotifier.NotifyTagsUpdated();
        logger.LogInformation("Deleted tag {TagId}", id);
    }

    public async Task<long> CreateTagModifierAsync(string name)
    {
        var modifier = new TagModifier(name.TrimOrThrowIfEmpty().ToLowerInvariant());
        await dbContext.TagModifiers.AddAsync(modifier);
        await SaveAsync();
        await updateNotifier.NotifyTagModifiersUpdated();
        logger.LogInformation("Created tag modifier {ModifierId} '{ModifierName}'", modifier.Id, modifier.Name);
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
        logger.LogInformation("Updated tag modifier {ModifierId}", id);
    }

    public async Task DeleteTagModifierAsync(long id)
    {
        var modifier = await dbContext.TagModifiers.FindAsync(id) ?? throw new ArgumentException("Modifier not found");
        modifier.IsDeleted = true;
        modifier.UpdatedAt = DateTime.UtcNow;
        await SaveAsync();
        await updateNotifier.NotifyTagModifiersUpdated();
        logger.LogInformation("Deleted tag modifier {ModifierId}", id);
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
        logger.LogInformation("Added alias to tag {TagId}", tagId);
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
            logger.LogInformation("Removed alias from tag {TagId}", tagId);
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
        logger.LogInformation("Added implication from tag {TagId} to tag {ImpliedTagId}", tagId, impliedTagId);
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
            logger.LogInformation("Removed implication from tag {TagId} to tag {ImpliedTagId}", tagId,
                impliedTagId);
        }
    }

    // Applied Tags
    public async Task<long> AddAppliedTagToMediaAsync(long mediaId, long tagId, List<long>? modifierIds,
        long? combinedWithAppliedTagId, string? combineWord)
    {
        var media = await dbContext.MediaFiles.Include(m => m.AppliedTags).FirstOrDefaultAsync(m => m.Id == mediaId) ??
                    throw new ArgumentException("Media not found");

        var appliedTag = await GetOrCreateAppliedTagAsync(tagId, modifierIds, combinedWithAppliedTagId, combineWord);

        if (media.AppliedTags.All(tag => tag.Id != appliedTag.Id))
        {
            media.AppliedTags.Add(appliedTag);
            await SaveAsync();
            await updateNotifier.NotifyMediaUpdated(mediaId);
            logger.LogDebug("Added applied tag {AppliedTagId} to media {MediaId}", appliedTag.Id, mediaId);
        }

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
            logger.LogDebug("Removed applied tag {AppliedTagId} from media {MediaId}", appliedTagId,
                mediaId);
        }
    }

    public async Task<long> AddAppliedTagToCollectionAsync(long collectionId, long tagId, List<long>? modifierIds,
        long? combinedWithAppliedTagId, string? combineWord)
    {
        var collection = await dbContext.Collections.Include(c => c.AppliedTags)
                             .FirstOrDefaultAsync(c => c.Id == collectionId) ??
                         throw new ArgumentException("Collection not found");

        var appliedTag = await GetOrCreateAppliedTagAsync(tagId, modifierIds, combinedWithAppliedTagId, combineWord);

        if (collection.AppliedTags.All(tag => tag.Id != appliedTag.Id))
        {
            collection.AppliedTags.Add(appliedTag);
            await SaveAsync();
            await updateNotifier.NotifyCollectionUpdated(collectionId);
            logger.LogInformation("Added applied tag {AppliedTagId} to collection '{CollectionName}' ({CollectionId})",
                appliedTag.Id, collection.Name, collection.Id);
        }

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
            logger.LogInformation(
                "Removed applied tag {AppliedTagId} from collection '{CollectionName}' ({CollectionId})",
                appliedTagId, collection.Name, collection.Id);
        }
    }

    public async Task DeleteOrphanedAppliedTagsAsync()
    {
        var orphanedAppliedTags = await dbContext.AppliedTags
            .IgnoreQueryFilters()
            .Where(appliedTag => !appliedTag.MediaFiles.Any() &&
                                 !appliedTag.Collections.Any() &&
                                 !appliedTag.UploadSections.Any() &&
                                 !appliedTag.ScannedCollections.Any() &&
                                 !appliedTag.FoundMedia.Any())
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
        logger.LogInformation("Created download gallery {GalleryId}", gallery.Id);
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
        logger.LogDebug("Updated download gallery {GalleryId}", id);
    }

    public async Task DeleteDownloadGalleryAsync(long id)
    {
        var gallery = await dbContext.DownloadGalleries.FindAsync(id) ??
                      throw new ArgumentException("Gallery not found");
        gallery.IsDeleted = true;
        gallery.UpdatedAt = DateTime.UtcNow;
        await SaveAsync();
        await updateNotifier.NotifyDownloadGalleriesUpdated();
        logger.LogInformation("Deleted download gallery {GalleryId}", id);
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
            .Include(t => t.CombinedWith)
            .ThenInclude(t => t!.Tag)
            .Include(t => t.CombinedWith)
            .ThenInclude(t => t!.Modifiers)
            .Where(t => t.MediaFiles.Any(m => m.Id == mediaId))
            .AsSplitQuery()
            .ToListAsync();
    }

    public async Task<List<AppliedTag>> GetCollectionAppliedTagsAsync(long collectionId)
    {
        return await dbContext.AppliedTags
            .Include(t => t.Tag)
            .Include(t => t.Modifiers)
            .Include(t => t.CombinedWith)
            .ThenInclude(t => t!.Tag)
            .Include(t => t.CombinedWith)
            .ThenInclude(t => t!.Modifiers)
            .Where(t => t.Collections.Any(c => c.Id == collectionId))
            .AsSplitQuery()
            .ToListAsync();
    }

    public async Task<List<AppliedTag>> GetUploadSectionAppliedTagsAsync(long sectionId)
    {
        return await dbContext.AppliedTags
            .Include(t => t.Tag)
            .Include(t => t.Modifiers)
            .Include(t => t.CombinedWith)
            .ThenInclude(t => t!.Tag)
            .Include(t => t.CombinedWith)
            .ThenInclude(t => t!.Modifiers)
            .Where(t => t.UploadSections.Any(s => s.Id == sectionId))
            .AsSplitQuery()
            .ToListAsync();
    }

    public async Task<long> AddAppliedTagToUploadSectionAsync(long sectionId, long tagId, List<long>? modifierIds,
        long? combinedWithAppliedTagId, string? combineWord)
    {
        var section = await dbContext.UploadSections.Include(s => s.AppliedTags)
            .FirstOrDefaultAsync(s => s.Id == sectionId) ?? throw new ArgumentException("Upload section not found");

        var appliedTag = await GetOrCreateAppliedTagAsync(tagId, modifierIds, combinedWithAppliedTagId, combineWord);
        if (section.AppliedTags.All(tag => tag.Id != appliedTag.Id))
        {
            section.AppliedTags.Add(appliedTag);
            await SaveAsync();
            await updateNotifier.NotifyUploadSectionUpdated(sectionId);
        }

        return appliedTag.Id;
    }

    public async Task RemoveAppliedTagFromUploadSectionAsync(long sectionId, long appliedTagId)
    {
        var section = await dbContext.UploadSections.Include(s => s.AppliedTags)
            .FirstOrDefaultAsync(s => s.Id == sectionId) ?? throw new ArgumentException("Upload section not found");

        var appliedTag = section.AppliedTags.FirstOrDefault(t => t.Id == appliedTagId);
        if (appliedTag != null)
        {
            section.AppliedTags.Remove(appliedTag);
            await SaveAsync();
            await updateNotifier.NotifyUploadSectionUpdated(sectionId);
        }
    }

    public async Task<long> AddParsedAppliedTagToMediaAsync(long mediaId, AppliedTagDTO appliedTag)
    {
        var media = await dbContext.MediaFiles.Include(item => item.AppliedTags)
            .FirstOrDefaultAsync(item => item.Id == mediaId) ?? throw new ArgumentException("Media not found");
        var storedTag = await GetOrCreateAppliedTagAsync(appliedTag);
        if (media.AppliedTags.All(tag => tag.Id != storedTag.Id))
        {
            media.AppliedTags.Add(storedTag);
            await SaveAsync();

            // TODO: notify image data updated
        }

        return storedTag.Id;
    }

    public async Task<long> AddParsedAppliedTagToCollectionAsync(long collectionId, AppliedTagDTO appliedTag)
    {
        var collection = await dbContext.Collections.Include(item => item.AppliedTags)
                             .FirstOrDefaultAsync(item => item.Id == collectionId) ??
                         throw new ArgumentException("Collection not found");
        var storedTag = await GetOrCreateAppliedTagAsync(appliedTag);
        if (collection.AppliedTags.All(tag => tag.Id != storedTag.Id))
        {
            collection.AppliedTags.Add(storedTag);
            await SaveAsync();

            // TODO: notify collection updated
        }

        return storedTag.Id;
    }

    public async Task<long> AddParsedAppliedTagToUploadSectionAsync(long sectionId, AppliedTagDTO appliedTag)
    {
        var section = await dbContext.UploadSections.Include(item => item.AppliedTags)
                          .FirstOrDefaultAsync(item => item.Id == sectionId) ??
                      throw new ArgumentException("Upload section not found");
        var storedTag = await GetOrCreateAppliedTagAsync(appliedTag);
        if (section.AppliedTags.All(tag => tag.Id != storedTag.Id))
        {
            section.AppliedTags.Add(storedTag);
            await SaveAsync();
            await updateNotifier.NotifyUploadSectionUpdated(sectionId);
        }

        return storedTag.Id;
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

    async Task<List<UploadSectionDTO>> IClientDatabaseService.GetUploadSectionsAsync()
    {
        return (await GetUploadSectionsAsync()).Select(section => section.GetDTO()).ToList();
    }

    async Task<List<RecentImportSectionDTO>> IClientDatabaseService.GetRecentImportSectionsAsync()
    {
        return await GetRecentImportSectionsAsync();
    }

    async Task<UploadSectionDTO?> IClientDatabaseService.GetUploadSectionAsync(long sectionId)
    {
        var section = await GetUploadSectionAsync(sectionId);
        if (section == null)
            return null;

        return section.GetDTO();
    }

    async Task<UploadSectionDTO> IClientDatabaseService.GetOrCreateUploadSectionAsync(string? name)
    {
        var section = await GetOrCreateUploadSectionAsync(name);
        return (await ((IClientDatabaseService)this).GetUploadSectionAsync(section.Id))!;
    }

    async Task IClientDatabaseService.SaveUploadSectionAsync(UploadSectionDTO request)
    {
        var section = await GetUploadSectionAsync(request.Id)
                      ?? throw new ArgumentException("Section not found");
        section.Name = request.Name;
        section.KeepTarget = request.KeepTarget;
        section.RemoveAfterImport = request.RemoveAfterImport;
        section.TargetFolderId = request.TargetFolderId;
        await SaveUploadSectionAsync(section);
    }

    Task IClientDatabaseService.DeleteUploadSectionAsync(long sectionId) => DeleteUploadSectionAsync(sectionId);

    Task IClientDatabaseService.RemoveMediaFromUploadSectionAsync(long sectionId, List<long> mediaIds) =>
        RemoveMediaFromUploadSectionAsync(sectionId, mediaIds);

    Task IClientDatabaseService.ReorderUploadSectionAsync(long sectionId, List<long> mediaIds) =>
        ReorderUploadSectionAsync(sectionId, mediaIds);

    Task IClientDatabaseService.SetUploadSectionActiveAsync(long? sectionId) => SetUploadSectionActiveAsync(sectionId);

    Task IClientDatabaseService.ImportUploadSectionAsync(long sectionId, List<long>? mediaIds) =>
        ImportUploadSectionAsync(sectionId, mediaIds);

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
        logger.LogDebug("Added ignored duplicate pair {FirstMediaId}, {SecondMediaId}", first, second);
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
            logger.LogInformation("Removed ignored duplicate pair {FirstMediaId}, {SecondMediaId}", first, second);
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

    async Task<AppliedTagDTO?> IClientDatabaseService.ParseTagAsync(string tag)
    {
        return (await new TagParser(this).ParseTag(tag))?.GetDTO();
    }

    async Task<List<string>> IClientDatabaseService.GetTagSuggestionsAsync(string search, int maxCount)
    {
        return await new TagParser(this).GetSuggestions(search, maxCount);
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

    async Task<List<AppliedTagDTO>> IClientDatabaseService.GetUploadSectionAppliedTagsAsync(long sectionId)
    {
        return (await GetUploadSectionAppliedTagsAsync(sectionId)).Select(t => t.GetDTO()).ToList();
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
        int pageSize, FolderSortColumn sortColumn, SortDirection sortDirection, string? searchText = null,
        bool recursive = false)
    {
        // Recursive mode works weirdly if the search is not provided
        recursive = recursive && !string.IsNullOrWhiteSpace(searchText);

        // Fetch subfolders and collections
        var subfolderQuery = dbContext.MediaFolders
            .Where(f => f.Parents.Any(p => p.Id == folderId) && !f.IsDeleted);

        var collectionQuery = dbContext.Collections
            .Where(c => c.Folders.Any(f => f.Id == folderId) && !c.IsDeleted);

        if (recursive)
        {
            var folders = await dbContext.MediaFolders
                .Where(f => !f.IsDeleted)
                .Include(f => f.Parents)
                .ToListAsync();
            var folderIds = new HashSet<long>
            {
                folderId,
            };
            bool foundNewFolder;

            do
            {
                foundNewFolder = false;
                foreach (var folder in folders)
                {
                    if (folder.Parents.Any(parent => folderIds.Contains(parent.Id)) && folderIds.Add(folder.Id))
                    {
                        foundNewFolder = true;
                    }
                }
            } while (foundNewFolder);

            subfolderQuery = dbContext.MediaFolders
                .Where(f => folderIds.Contains(f.Id) && f.Id != folderId && !f.IsDeleted);
            collectionQuery = dbContext.Collections
                .Where(c => c.Folders.Any(f => folderIds.Contains(f.Id)) && !c.IsDeleted);
        }

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

    private async Task ValidateCollectionRemovalAsync(long collectionId, IReadOnlyCollection<long> mediaIds)
    {
        var collection = await dbContext.Collections.FindAsync(collectionId) ??
                         throw new ArgumentException("Collection not found");
        if (collection.ImageGroupSize <= 1)
            return;

        var activeItemCount = await dbContext.Set<CollectionItem>()
            .Where(item => item.CollectionId == collectionId && !item.MediaFile.IsDeleted)
            .CountAsync();
        var removedActiveItemCount = await dbContext.Set<CollectionItem>()
            .Where(item => item.CollectionId == collectionId && mediaIds.Contains(item.MediaFileId) &&
                           !item.MediaFile.IsDeleted)
            .CountAsync();
        var remainingItemCount = activeItemCount - removedActiveItemCount;
        if (remainingItemCount % collection.ImageGroupSize != 0)
        {
            throw new InvalidOperationException(
                $"The collection requires images to remain in groups of {collection.ImageGroupSize}. " +
                $"Removing these images would leave {remainingItemCount} images.");
        }
    }

    private async Task AddRecentImportSectionAsync(string name)
    {
        var lowercaseName = name.ToLowerInvariant();
        var recentSection = await dbContext.RecentImportSections
            .FirstOrDefaultAsync(section => section.NameLowercase == lowercaseName);

        if (recentSection == null)
        {
            recentSection = new RecentImportSection(name);
            await dbContext.RecentImportSections.AddAsync(recentSection);
        }

        recentSection.LastUsed = DateTime.UtcNow;

        var oldSections = await dbContext.RecentImportSections
            .OrderByDescending(section => section.LastUsed)
            .ThenByDescending(section => section.Id)
            .Skip(100)
            .ToListAsync();
        if (oldSections.Count > 0)
        {
            dbContext.RecentImportSections.RemoveRange(oldSections);
        }

        // Saving just once here at the end to save on some DB writes
        await SaveAsync();
    }

    private async Task<AppliedTag> GetOrCreateAppliedTagAsync(long tagId, List<long>? modifierIds,
        long? combinedWithAppliedTagId, string? combineWord)
    {
        var requestedModifierIds = modifierIds?.Distinct().OrderBy(id => id).ToList() ?? [];
        var candidates = await dbContext.AppliedTags.Include(tag => tag.Modifiers)
            .Where(tag => tag.TagId == tagId && tag.CombinedWithId == combinedWithAppliedTagId &&
                          tag.CombineWord == combineWord)
            .ToListAsync();
        var existing = candidates.FirstOrDefault(tag => tag.Modifiers.Select(modifier => modifier.Id)
            .OrderBy(id => id).SequenceEqual(requestedModifierIds));
        if (existing != null)
            return existing;

        var appliedTag = new AppliedTag(tagId)
        {
            CombinedWithId = combinedWithAppliedTagId,
            CombineWord = combineWord,
        };
        if (requestedModifierIds.Count > 0)
        {
            var modifiers = await dbContext.TagModifiers.Where(modifier => requestedModifierIds.Contains(modifier.Id))
                .ToListAsync();
            foreach (var modifier in modifiers)
                appliedTag.Modifiers.Add(modifier);
        }

        dbContext.AppliedTags.Add(appliedTag);
        return appliedTag;
    }

    private async Task<AppliedTag> GetOrCreateAppliedTagAsync(AppliedTagDTO dto)
    {
        var combinedWithId = dto.CombinedWith == null
            ? dto.CombinedWithId
            : await GetOrCreateAppliedTagIdAsync(dto.CombinedWith);
        return await GetOrCreateAppliedTagAsync(dto.TagId, dto.Modifiers.Select(modifier => modifier.Id).ToList(),
            combinedWithId, dto.CombineWord);
    }

    private async Task<long> GetOrCreateAppliedTagIdAsync(AppliedTagDTO dto)
    {
        var tag = await GetOrCreateAppliedTagAsync(dto);
        if (tag.Id == 0)
            await SaveAsync();
        return tag.Id;
    }
}
