using DualView.Shared.Models;
using DualView.Shared.Models.DTO;
using DualView.Shared.Models.Enums;

namespace DualView.Shared.Services;

/// <summary>
///   Access to reading and some writing operations to the database (accessible from the server and the clients)
/// </summary>
public interface IDatabaseCommonService
{
    // Settings
    public Task<DualViewSettings> GetAppSettingsAsync();
    public Task SaveAppSettingsAsync(DualViewSettings settings);

    // Note that going forward many methods are split between the client and server base interfaces due to DTO-usage.
    // The relevant interfaces are IClientDatabaseService (client) and IDatabaseService (server).

    // Media
    public Task<long> CreateMediaFolder(string folderName, long parentId);
    public Task RenameMediaFolder(long folderId, string folderName);
    public Task<long> CreateCollection(string collectionName, long folderId);
    public Task RenameCollection(long collectionId, string collectionName);
    public Task SetCollectionImageGroupSizeAsync(long collectionId, int imageGroupSize);

    public Task AddCollectionToFolder(long collectionId, long folderId);
    public Task AddFolderToFolder(long folderId, long parentFolderId);

    public Task RemoveCollectionFromFolder(long collectionId, long folderId);
    public Task RemoveFolderFromFolder(long folderId, long parentFolderId);

    public Task AddMediaToCollection(List<long> mediaIds, long collectionId, int firstSequenceNumber,
        List<int>? sequenceNumbers = null);

    public Task RemoveMediaFromCollection(long mediaId, long collectionId);
    public Task ReorderCollection(long collectionId, List<long> newImageOrderIds);

    public Task<List<long>> GetMediaCollectionsAsync(long mediaId);

    /// <summary>
    ///   Pages API to get a subset of collections in a folder
    /// </summary>
    public Task<Tuple<List<CollectionDTO>, int>> GetFolderCollections(long folderId, int page, int pageSize);

    /// <summary>
    ///   Pages API to get a subset of media in a collection
    /// </summary>
    public Task<Tuple<List<MediaFileDTO>, int>> GetCollectionContents(long collectionId, int page, int pageSize,
        CollectionSortColumn sortColumn = CollectionSortColumn.CollectionOrder,
        SortDirection sortDirection = SortDirection.Ascending, string? search = null);

    public Task<bool> IsMediaSafeToDeleteAsync(long mediaId);
    public Task DeleteMediaAsync(long mediaId);
    public Task RestoreMediaAsync(long mediaId);
    public Task PurgeMediaAsync(long mediaId);

    public Task DeleteMediaFolderAsync(long folderId);
    public Task RestoreMediaFolderAsync(long folderId);
    public Task PurgeMediaFolderAsync(long folderId);

    public Task DeleteCollectionAsync(long collectionId);
    public Task<int> GetCollectionOrphanedMediaCountAsync(long collectionId);
    public Task RestoreCollectionAsync(long collectionId);
    public Task PurgeCollectionAsync(long collectionId);

    public Task<bool> SetMediaRatingAsync(long mediaId, bool isFavorited, int stars);

    // Upload Sections
    public Task AddMediaToActiveUploadSectionAsync(List<long> mediaIds);
    public Task AddMediaToUploadSectionAsync(long mediaId, long sectionId, int index);
    public Task<int> GetNextUploadSectionIndexAsync(long sectionId);
    public Task SetMediaTemporaryStatusAsync(long mediaId, bool isTemporary);
    public Task<List<string>> SearchUploadTargetNamesAsync(string search, int limit = 100);

    // Tags
    public Task<long> CreateTagAsync(string name, TagCategory category);

    public Task UpdateTagAsync(long id, string? name, string? description, TagCategory? category,
        long? exampleMediaId);

    public Task DeleteTagAsync(long id);

    public Task<long> CreateTagModifierAsync(string name);
    public Task UpdateTagModifierAsync(long id, string? name, string? description);
    public Task DeleteTagModifierAsync(long id);

    public Task<List<string>> GetTagAliasesAsync(long tagId);
    public Task<List<TagDTO>> GetTagImpliesAsync(long tagId);

    public Task CreateTagAliasAsync(long tagId, string alias);
    public Task DeleteTagAliasAsync(long tagId, string alias);

    public Task AddTagImplicationAsync(long tagId, long impliedTagId);
    public Task RemoveTagImplicationAsync(long tagId, long impliedTagId);

    // Applied Tags
    public Task<long> AddAppliedTagToMediaAsync(long mediaId, long tagId, List<long>? modifierIds,
        long? combinedWithAppliedTagId, string? combineWord);

    public Task RemoveAppliedTagFromMediaAsync(long mediaId, long appliedTagId);

    public Task<long> AddAppliedTagToCollectionAsync(long collectionId, long tagId, List<long>? modifierIds,
        long? combinedWithAppliedTagId, string? combineWord);

    public Task RemoveAppliedTagFromCollectionAsync(long collectionId, long appliedTagId);
    public Task<long> AddAppliedTagToUploadSectionAsync(long sectionId, long tagId, List<long>? modifierIds,
        long? combinedWithAppliedTagId, string? combineWord);
    public Task RemoveAppliedTagFromUploadSectionAsync(long sectionId, long appliedTagId);
    public Task<long> AddParsedAppliedTagToMediaAsync(long mediaId, AppliedTagDTO appliedTag);
    public Task<long> AddParsedAppliedTagToCollectionAsync(long collectionId, AppliedTagDTO appliedTag);
    public Task<long> AddParsedAppliedTagToUploadSectionAsync(long sectionId, AppliedTagDTO appliedTag);

    // Download Galleries
    public Task<long> CreateDownloadGalleryAsync(string galleryUrl);

    public Task UpdateDownloadGalleryAsync(long id, string? targetPath, string? galleryName, bool? isDownloaded,
        string? tagsString);

    public Task DeleteDownloadGalleryAsync(long id);
}
