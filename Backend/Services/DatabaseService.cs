using System.Text;
using DualView.Shared.Models;
using DualView.Shared.Models.DTO;
using DualView.Shared.Models.Enums;
using DualView.Shared.Services;
using DualView.Shared.Utils;
using Backend.Database;
using Backend.Models;
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
        await updateNotifier.NotifyMediaFolderContentsUpdated(folderId);
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
        await updateNotifier.NotifyCollectionContentsUpdated(collectionId);
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
        await updateNotifier.NotifyCollectionUpdated(collection.Id);
    }

    public async Task DeleteCollectionAsync(Collection collection)
    {
        collection.IsDeleted = true;
        await SaveAsync();
        await updateNotifier.NotifyMediaFolderContentsUpdated(collection.FolderId);
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
        var tag = new Tag(name.TrimOrThrowIfEmpty(), category);
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
            tag.Name = name.TrimOrThrowIfEmpty();

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
        await SaveAsync();
        await updateNotifier.NotifyTagsUpdated();
    }

    public async Task<long> CreateTagModifierAsync(string name)
    {
        var modifier = new TagModifier(name.TrimOrThrowIfEmpty());
        await dbContext.TagModifiers.AddAsync(modifier);
        await SaveAsync();
        await updateNotifier.NotifyTagModifiersUpdated();
        return modifier.Id;
    }

    public async Task UpdateTagModifierAsync(long id, string? name, string? description)
    {
        var modifier = await dbContext.TagModifiers.FindAsync(id) ?? throw new ArgumentException("Modifier not found");

        if (name != null)
            modifier.Name = name.TrimOrThrowIfEmpty();

        if (description != null)
            modifier.Description = description;

        await SaveAsync();
        await updateNotifier.NotifyTagModifiersUpdated();
    }

    public async Task DeleteTagModifierAsync(long id)
    {
        var modifier = await dbContext.TagModifiers.FindAsync(id) ?? throw new ArgumentException("Modifier not found");
        modifier.IsDeleted = true;
        await SaveAsync();
        await updateNotifier.NotifyTagModifiersUpdated();
    }

    public async Task CreateTagAliasAsync(long tagId, string alias)
    {
        var tagAlias = new TagAlias(alias.TrimOrThrowIfEmpty(), tagId);
        await dbContext.TagAliases.AddAsync(tagAlias);
        await SaveAsync();
        await updateNotifier.NotifyTagUpdated(tagId);
    }

    public async Task DeleteTagAliasAsync(long tagId, string alias)
    {
        var tagAlias = await dbContext.TagAliases.FirstOrDefaultAsync(a => a.TagId == tagId && a.Name == alias);
        if (tagAlias != null)
        {
            dbContext.TagAliases.Remove(tagAlias);
            await SaveAsync();
            await updateNotifier.NotifyTagUpdated(tagId);
        }
    }

    public async Task CreateTagModifierAliasAsync(long modifierId, string alias)
    {
        var modifierAlias = new TagModifierAlias(alias.TrimOrThrowIfEmpty(), modifierId);
        await dbContext.TagModifierAliases.AddAsync(modifierAlias);
        await SaveAsync();
        await updateNotifier.NotifyTagModifiersUpdated();
    }

    public async Task DeleteTagModifierAliasAsync(long modifierId, string alias)
    {
        var modifierAlias =
            await dbContext.TagModifierAliases.FirstOrDefaultAsync(a => a.ModifierId == modifierId && a.Name == alias);
        if (modifierAlias != null)
        {
            dbContext.TagModifierAliases.Remove(modifierAlias);
            await SaveAsync();
            await updateNotifier.NotifyTagModifiersUpdated();
        }
    }

    public async Task AddTagImplicationAsync(long tagId, long impliedTagId)
    {
        if (tagId == impliedTagId)
            return;

        var alreadyExists = await dbContext.TagImplies.AnyAsync(i => i.PrimaryTagId == tagId && i.ToApplyTagId == impliedTagId);
        if (alreadyExists)
            return;

        var imply = new TagImply(tagId, impliedTagId);
        await dbContext.TagImplies.AddAsync(imply);
        await SaveAsync();
        await updateNotifier.NotifyTagUpdated(tagId);
    }

    public async Task RemoveTagImplicationAsync(long tagId, long impliedTagId)
    {
        var imply = await dbContext.TagImplies.FirstOrDefaultAsync(i => i.PrimaryTagId == tagId && i.ToApplyTagId == impliedTagId);
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
        var gallery = await dbContext.DownloadGalleries.FindAsync(id) ?? throw new ArgumentException("Gallery not found");

        if (targetPath != null)
            gallery.TargetPath = targetPath;

        if (galleryName != null)
            gallery.GalleryName = galleryName;

        if (isDownloaded != null)
            gallery.IsDownloaded = isDownloaded.Value;

        if (tagsString != null)
            gallery.TagsString = tagsString;

        await SaveAsync();
        await updateNotifier.NotifyDownloadGalleryUpdated(id);
    }

    public async Task DeleteDownloadGalleryAsync(long id)
    {
        var gallery = await dbContext.DownloadGalleries.FindAsync(id) ?? throw new ArgumentException("Gallery not found");
        gallery.IsDeleted = true;
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

    public async Task<MediaImportInfo?> GetMediaImportInfoAsync(long mediaId)
    {
        return await dbContext.MediaImportInfos.FirstOrDefaultAsync(i => i.MediaFileId == mediaId);
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
