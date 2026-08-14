using DualView.Shared.Services;
using DualView.Shared.Models.DTO;
using DualView.Shared.Models.Enums;
using Backend.Models;

namespace Backend.Services;

/// <summary>
///   Full database access for purely on the server side. Also acts as the server equivalent of
///   <see cref="IClientDatabaseService"/>. All methods there must be also here
///   (implemented as calling our method variants and converting the results to DTO if needed).
/// </summary>
public interface IDatabaseService : IDatabaseCommonService, IDisposable
{
    public Task InitializeDatabaseAsync();

    /// <summary>
    ///   Saves all entity framework changes. Note that this won't send client notifications!
    /// </summary>
    /// <returns>Task</returns>
    public Task SaveAsync();

    // Media folders
    public Task<List<MediaFolder>> GetMediaFoldersAsync(long? limitToParent = null);
    public Task<MediaFolder?> GetMediaFolderAsync(long id);
    public Task<MediaFolder?> GetMediaFolderAsync(string name, long? parentFolderId);

    public Task<MediaFolder> CreateMediaFolderAsync(string folderName, long parentId);
    public Task SaveMediaFolderAsync(MediaFolder folder);
    public Task<MediaFolder?> GetMediaFolderFromPathAsync(string path);

    public Task<List<Collection>> GetCollectionsInFolderAsync(long folderId);
    public Task<Collection?> GetCollectionByNameAndFolder(string name, long folderId);

    // Collections
    public Task<Collection?> GetCollectionAsync(long id);
    public Task<MediaFile?> GetCollectionPreviewMediaAsync(long collectionId);
    public Task<List<MediaFile>> GetCollectionContents(long collectionId);
    public Task<List<FolderPathDTO>> GetCollectionFolderPaths(long collectionId);
    public Task SaveCollectionAsync(Collection collection);
    public Task DeleteCollectionAsync(Collection collection);

    // Media
    public Task<MediaFile?> GetMediaByHashAsync(string hash);
    public Task<MediaFile?> GetMediaByIdAsync(long id);

    public Task SaveMediaFileAsync(MediaFile mediaFile);
    public Task<List<long>> GetAllMediaFileIdsAsync();
    public Task<List<long>> GetOrphanedMediaFilesAsync();
    public Task<List<Collection>> GetOrphanedCollectionsAsync();
    public Task<List<MediaFolder>> GetOrphanedMediaFoldersAsync();

    public Task<int> GetNextCollectionSequenceNumberAsync(long collectionId);

    public Task<List<MediaFile>> GetMediaFileSiblingsAsync(long mediaId);

    public Task<CollectionMediaRemovalPreview> PreviewCollectionMediaRemovalAsync(long collectionId,
        List<long> mediaIds);
    public Task<CollectionMediaRemovalResult> RemoveMediaFromCollectionAsync(long collectionId, List<long> mediaIds);
    public Task UndoCollectionMediaRemovalAsync(CollectionMediaRemovalResult removal);
    public Task<CollectionMediaRemovalResult> DeleteCollectionAndImagesAsync(long collectionId);
    public Task<List<Collection>> GetEligibleCollectionsForPurgeAsync(TimeSpan timeSinceDeletion);

    public Task<List<MediaFile>> GetDeletedMediaAsync(int limit);
    public Task<List<MediaFolder>> GetDeletedMediaFoldersAsync(int limit);
    public Task<List<Collection>> GetDeletedCollectionsAsync(int limit);
    public Task<List<FolderPathDTO>> GetFolderParentFolderPaths(long folderId);
    public Task<string> GetMediaFolderPath(long folderId);

    public Task<Tuple<List<ConfiguredMediaInfo>, int>> GetMediaFolderContents(long folderId, int itemPage, int pageSize,
        FolderSortColumn sortColumn, SortDirection sortDirection, string? searchText = null);

    /// <summary>
    ///   Creates a new media file and adds it to a collection
    /// </summary>
    public Task<MediaFile> CreateMediaAsync(MediaFile mediaItem, long collectionId);

    /// <summary>
    ///   Creates a new media file and adds it to an upload section
    /// </summary>
    public Task<MediaFile> CreateMediaAsync(MediaFile mediaItem, string? sectionName);

    public Task<List<MediaFile>> GetEligibleMediaFilesForPurgeAsync(TimeSpan timeSinceDeletion);
    public Task<List<MediaFile>> GetEligibleTemporaryMediaFilesForPurgeAsync(TimeSpan timeSinceUpdate);

    // Upload sections
    public Task<UploadSection> GetOrCreateUploadSectionAsync(string? sectionName);
    public Task<List<UploadSection>> GetUploadSectionsAsync();
    public Task SaveUploadSectionAsync(UploadSection section);
    public Task RemoveMediaFromUploadSectionAsync(long sectionId, List<long> mediaIds);
    public Task ReorderUploadSectionAsync(long sectionId, List<long> mediaIds);
    public Task SetUploadSectionActiveAsync(long? sectionId);
    public Task ImportUploadSectionAsync(long sectionId, List<long>? mediaIds);

    // Backend maintenance jobs
    public Task<MaintenanceJobRecord?> GetMaintenanceRecord(string name);
    public Task<MaintenanceJobRecord?> GetOldestMaintenanceJobToRun(DateTime afterTime);
    public Task CreateMaintenanceRecord(MaintenanceJobRecord record);
    public Task SaveMaintenanceRecord(MaintenanceJobRecord record);
    public Task DeleteMaintenanceRecord(MaintenanceJobRecord record);

    // Transaction handling to bundle operations
    public Task BeginTransaction();
    public Task CommitTransaction();
    public Task RollbackTransaction();

    // Advanced reloading
    public Task ReloadEntity(MediaFile mediaFile);

    // Tags
    public Task<List<Tag>> GetTagsAsync();
    public Task<Tag?> GetTagAsync(long id);
    public Task<Tag?> GetTagByNameAsync(string name);
    public Task<List<TagModifier>> GetTagModifiersAsync();
    public Task<TagModifier?> GetTagModifierAsync(long id);
    public Task<TagModifier?> GetTagModifierByNameAsync(string name);

    // Applied Tags
    public Task<List<AppliedTag>> GetMediaAppliedTagsAsync(long mediaId);
    public Task<List<AppliedTag>> GetCollectionAppliedTagsAsync(long collectionId);
    public Task<AppliedTag?> GetAppliedTagAsync(long id);
    public Task DeleteOrphanedAppliedTagsAsync();

    public Task<Tag?> GetTagByNameOrAliasAsync(string name);
    public Task<TagBreakRule?> GetTagBreakRuleByStrAsync(string str);
    public Task<string?> GetTagSuperAliasAsync(string alias);

    public Task<List<Tag>> SearchTagsWildcardAsync(string search);
    public Task<List<string>> SelectTagNamesWildcardAsync(string pattern, int maxCount = 50);
    public Task<List<string>> SelectTagAliasesWildcardAsync(string pattern, int maxCount = 50);
    public Task<List<string>> SelectTagModifierNamesWildcardAsync(string pattern, int maxCount = 50);
    public Task<List<string>> SelectTagBreakRulesByStrWildcardAsync(string pattern, int maxCount = 50);
    public Task<List<string>> SelectTagSuperAliasWildcardAsync(string pattern, int maxCount = 50);

    // Import & Galleries
    public Task<MediaImportInfo?> GetMediaImportInfoAsync(long mediaId);
    public Task SaveMediaImportInfoAsync(MediaImportInfo importInfo);
    public Task<List<DownloadGallery>> GetDownloadGalleriesAsync();
    public Task<DownloadGallery?> GetDownloadGalleryAsync(long id);
    public Task<DownloadGallery?> GetDownloadGalleryByUrlAsync(string url);

    // Ignored Duplicates
    public Task AddIgnoredDuplicateAsync(long mediaId1, long mediaId2);
    public Task RemoveIgnoredDuplicateAsync(long mediaId1, long mediaId2);
    public Task<bool> IsIgnoredDuplicateAsync(long mediaId1, long mediaId2);
}
