using DualView.Shared.Models;
using DualView.Shared.Models.DTO;
using DualView.Shared.Models.Enums;

namespace DualView.Shared.Services;

/// <summary>
///   Client-specific database access using client types. Must have equivalent methods that IDatabaseService has.
/// </summary>
public interface IClientDatabaseService : IDatabaseCommonService
{
    // Media
    public Task<List<MediaFolderInfo>> GetMediaFoldersAsync(long? limitToParent = null);
    public Task<MediaFolderDTO?> GetMediaFolderAsync(long id);
    public Task<MediaFolderDTO?> GetMediaFolderFromPathAsync(string path);
    public Task<MediaFileDTO?> GetMediaFileAsync(long mediaId);
    public Task<CollectionDTO?> GetCollectionAsync(long collectionId);
    public Task<List<MediaFileDTO>> GetCollectionContents(long collectionId);
    public Task<CollectionBrowseInfoDTO> GetCollectionBrowseInfoAsync(long collectionId, long? mediaId = null);
    public Task<MediaFileDTO?> GetCollectionMediaAtIndexAsync(long collectionId, int index);
    public Task<List<MediaFileDTO>> GetMediaFileSiblingsAsync(long mediaId);

    public Task<CollectionMediaRemovalPreview> PreviewCollectionMediaRemovalAsync(long collectionId,
        List<long> mediaIds);
    public Task<CollectionMediaRemovalResult> RemoveMediaFromCollectionAsync(long collectionId, List<long> mediaIds);
    public Task UndoCollectionMediaRemovalAsync(CollectionMediaRemovalResult removal);
    public Task<CollectionMediaRemovalResult> DeleteCollectionAndImagesAsync(long collectionId);

    public Task<List<FolderPathDTO>> GetCollectionFolderPaths(long collectionId);
    public Task<List<FolderPathDTO>> GetFolderParentFolderPaths(long folderId);
    public Task<string> GetMediaFolderPath(long folderId);

    public Task<MediaFileDTO> CreateMediaFileAsync(MediaFileDTO mediaFile, long collectionId);
    public Task SaveMediaFileAsync(MediaFileDTO media);

    public Task<List<MediaFileDTO>> GetDeletedMediaAsync(int limit, int offset = 0);
    public Task<List<MediaFolderDTO>> GetDeletedMediaFoldersAsync(int limit);
    public Task<List<CollectionDTO>> GetDeletedCollectionsAsync(int limit);

    // TODO: remove these when confirmed are unneeded
    // Compatibility methods
    public Task<ConfiguredMediaDTO?> GetConfiguredMediaAsync(long mediaConfigId, bool isView = true);
    public Task<MediaConfigFolderInfo> GetConfiguredMediaFoldersAsync(long mediaConfigId);
    public Task<List<ConfiguredMediaDTO>> GetConfiguredMediaSiblingsAsync(long mediaConfigId);
    public Task<Tuple<List<ConfiguredMediaInfo>, int>> GetMediaFolderContents(long folderId, int itemPage, int pageSize,
        FolderSortColumn sortColumn, SortDirection sortDirection, string? searchText = null, bool recursive = false);
    public Task AddMediaToFolder(long mediaConfigurationId, string folderPath, bool canCreateRootFolder = false);
    public Task RemoveMediaFromFolder(long mediaConfigurationId, string folderPath);
    public Task<ConfiguredMediaDTO> CreateConfiguredMediaAsync(long mediaId, string configName, List<string> folders);
    public Task SaveConfiguredMediaAsync(ConfiguredMediaDTO media);
    
    // Tags
    public Task<List<TagDTO>> GetAllTagsAsync();
    public Task<TagDTO?> GetTagAsync(long id);
    public Task<List<TagModifierDTO>> GetAllTagModifiersAsync();
    public Task<TagModifierDTO?> GetTagModifierAsync(long id);

    // Applied Tags
    public Task<List<AppliedTagDTO>> GetMediaAppliedTagsAsync(long mediaId);
    public Task<List<AppliedTagDTO>> GetCollectionAppliedTagsAsync(long collectionId);
    public Task<List<AppliedTagDTO>> GetUploadSectionAppliedTagsAsync(long sectionId);
    public Task<AppliedTagDTO?> ParseTagAsync(string tag);
    public Task<List<string>> GetTagSuggestionsAsync(string search, int maxCount = 100);

    // Tag search
    public Task<List<TagDTO>> SearchTagsWildcardAsync(string search);
    public Task<TagDTO?> GetTagByNameAsync(string name);

    // Import & Galleries
    public Task<MediaImportInfoDTO?> GetMediaImportInfoAsync(long mediaId);
    public Task<List<UploadSectionDTO>> GetUploadSectionsAsync();
    public Task<List<RecentImportSectionDTO>> GetRecentImportSectionsAsync();
    public Task<UploadSectionDTO?> GetUploadSectionAsync(long sectionId);
    public Task<UploadSectionDTO> CloneUploadSectionAsync(long sectionId);
    public Task<UploadSectionDTO> GetOrCreateUploadSectionAsync(string? name);
    public Task SaveUploadSectionAsync(UploadSectionDTO section);
    public Task DeleteUploadSectionAsync(long sectionId);
    public Task RemoveMediaFromUploadSectionAsync(long sectionId, List<long> mediaIds);
    public Task ReorderUploadSectionAsync(long sectionId, List<long> mediaIds);
    public Task SetUploadSectionActiveAsync(long? sectionId);
    public Task ImportUploadSectionAsync(long sectionId, List<long>? mediaIds);
    public Task<List<DownloadGalleryDTO>> GetAllDownloadGalleriesAsync();
    public Task<DownloadGalleryDTO?> GetDownloadGalleryAsync(long id);
}
