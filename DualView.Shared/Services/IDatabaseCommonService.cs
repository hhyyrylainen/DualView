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
    public Task<long> CreateMediaFolder(string folderName, long? parentId);
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
}
