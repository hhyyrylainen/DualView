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
    public Task<long> CreateCollection(string collectionName, long folderId);

    public Task AddMediaToCollection(long mediaId, long collectionId, int sequenceNumber);
    public Task RemoveMediaFromCollection(long mediaId, long collectionId);

    public Task<List<long>> GetMediaCollectionsAsync(long mediaId);

    /// <summary>
    ///   Pages API to get a subset of collections in a folder
    /// </summary>
    public Task<Tuple<List<CollectionDTO>, int>> GetFolderCollections(long folderId, int page, int pageSize);

    /// <summary>
    ///   Pages API to get a subset of media in a collection
    /// </summary>
    public Task<Tuple<List<MediaFileDTO>, int>> GetCollectionContents(long collectionId, int page, int pageSize,
        FolderSortColumn sortColumn, SortDirection sortDirection);

    /// <summary>
    ///   Updates media keep status.
    /// </summary>
    /// <param name="mediaId">Media to update</param>
    /// <param name="keep">New keep value</param>
    /// <returns>True if modified, false if status was already right</returns>
    public Task<bool> SetMediaKeepStatusAsync(long mediaId, bool keep);

    public Task<bool> IsMediaSafeToDeleteAsync(long mediaId);
    public Task DeleteMediaAsync(long mediaId);
    public Task RestoreMediaAsync(long mediaId);
    public Task PurgeMediaAsync(long mediaId);

    public Task DeleteMediaFolderAsync(long folderId);
    public Task RestoreMediaFolderAsync(long folderId);
    public Task PurgeMediaFolderAsync(long folderId);

    public Task DeleteCollectionAsync(long collectionId);
    public Task RestoreCollectionAsync(long collectionId);
    public Task PurgeCollectionAsync(long collectionId);

    public Task<bool> SetMediaRatingAsync(long mediaId, bool isFavorited, int stars);

    // Tags
    public Task<long> CreateTagAsync(string name, TagCategory category);
    public Task UpdateTagAsync(long id, string? name, string? description, TagCategory? category,
        long? exampleMediaId);
    public Task DeleteTagAsync(long id);

    public Task<long> CreateTagModifierAsync(string name);
    public Task UpdateTagModifierAsync(long id, string? name, string? description);
    public Task DeleteTagModifierAsync(long id);

    public Task CreateTagAliasAsync(long tagId, string alias);
    public Task DeleteTagAliasAsync(long tagId, string alias);

    public Task CreateTagModifierAliasAsync(long modifierId, string alias);
    public Task DeleteTagModifierAliasAsync(long modifierId, string alias);

    public Task AddTagImplicationAsync(long tagId, long impliedTagId);
    public Task RemoveTagImplicationAsync(long tagId, long impliedTagId);

    // Applied Tags
    public Task<long> AddAppliedTagToMediaAsync(long mediaId, long tagId, List<long>? modifierIds,
        long? combinedWithAppliedTagId, string? combineWord);
    public Task RemoveAppliedTagFromMediaAsync(long mediaId, long appliedTagId);

    public Task<long> AddAppliedTagToCollectionAsync(long collectionId, long tagId, List<long>? modifierIds,
        long? combinedWithAppliedTagId, string? combineWord);
    public Task RemoveAppliedTagFromCollectionAsync(long collectionId, long appliedTagId);

    // Download Galleries
    public Task<long> CreateDownloadGalleryAsync(string galleryUrl);
    public Task UpdateDownloadGalleryAsync(long id, string? targetPath, string? galleryName, bool? isDownloaded,
        string? tagsString);
    public Task DeleteDownloadGalleryAsync(long id);
}
