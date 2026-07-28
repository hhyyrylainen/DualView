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
    public Task AddMediaToFolder(long mediaConfigurationId, string folderPath, bool canCreateRootFolder = false);
    public Task RemoveMediaFromFolder(long mediaConfigurationId, string folderPath);

    public Task<MediaConfigFolderInfo> GetConfiguredMediaFoldersAsync(long mediaConfigId);

    /// <summary>
    ///   Pages API to get a subset of media in a folder
    /// </summary>
    public Task<Tuple<List<ConfiguredMediaInfo>, int>> GetMediaFolderContents(long folderId, int itemPage, int pageSize,
        FolderSortColumn sortColumn, SortDirection sortDirection);

    /// <summary>
    ///   Updates media keep status.
    /// </summary>
    /// <param name="configuredMediaId">Configured media to update</param>
    /// <param name="keep">New keep value</param>
    /// <returns>True if modified, false if status was already right</returns>
    public Task<bool> SetMediaKeepStatusAsync(long configuredMediaId, bool keep);

    public Task<bool> IsMediaSafeToDeleteAsync(long configuredMediaId);
    public Task DeleteMediaAsync(long configuredMediaId);

    public Task RestoreMediaAsync(long configuredMediaId);
}
