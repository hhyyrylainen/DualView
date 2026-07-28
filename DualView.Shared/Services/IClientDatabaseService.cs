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
    public Task<List<MediaFileDTO>> GetMediaFileSiblingsAsync(long mediaId);

    public Task<MediaFileDTO> CreateMediaFileAsync(MediaFileDTO mediaFile, long collectionId);
    public Task SaveMediaFileAsync(MediaFileDTO media);

    public Task<List<MediaFileDTO>> GetDeletedMediaAsync(int limit);

    // TODO: remove these when confirmed are unneeded
    // Compatibility methods
    public Task<ConfiguredMediaDTO?> GetConfiguredMediaAsync(long mediaConfigId);
    public Task<MediaConfigFolderInfo> GetConfiguredMediaFoldersAsync(long mediaConfigId);
    public Task<List<ConfiguredMediaDTO>> GetConfiguredMediaSiblingsAsync(long mediaConfigId);
    public Task<Tuple<List<ConfiguredMediaInfo>, int>> GetMediaFolderContents(long folderId, int itemPage, int pageSize,
        FolderSortColumn sortColumn, SortDirection sortDirection);
    public Task AddMediaToFolder(long mediaConfigurationId, string folderPath, bool canCreateRootFolder = false);
    public Task RemoveMediaFromFolder(long mediaConfigurationId, string folderPath);
    public Task<ConfiguredMediaDTO> CreateConfiguredMediaAsync(long mediaId, string configName, List<string> folders);
    public Task SaveConfiguredMediaAsync(ConfiguredMediaDTO media);
    public Task<ConfiguredMediaDTO?> GetPrimeConfiguredMediaFromFileAsync(long mediaFileId);
}
