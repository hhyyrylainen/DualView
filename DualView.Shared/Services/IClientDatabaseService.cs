using DualView.Shared.Models.DTO;

namespace DualView.Shared.Services;

/// <summary>
///   Client-specific database access using client types. Must have equivalent methods that IDatabaseService has.
/// </summary>
public interface IClientDatabaseService : IDatabaseCommonService
{
    // Media
    public Task<List<MediaStorageFolderInfo>> GetMediaFoldersAsync(long? limitToParent = null);
    public Task<MediaStorageFolderDTO?> GetMediaFolderAsync(long id);
    public Task<MediaStorageFolderDTO?> GetMediaFolderFromPathAsync(string path);
    public Task<ConfiguredMediaDTO?> GetConfiguredMediaAsync(long mediaConfigId);
    public Task<List<ConfiguredMediaDTO>> GetConfiguredMediaSiblingsAsync(long mediaConfigId);

    public Task<ConfiguredMediaDTO> CreateConfiguredMediaAsync(long mediaId, string configName, List<string> folders);
    public Task SaveConfiguredMediaAsync(ConfiguredMediaDTO media);

    public Task<ConfiguredMediaDTO?> GetPrimeConfiguredMediaFromFileAsync(long mediaFileId);

    public Task<List<ConfiguredMediaDTO>> GetDeletedMediaAsync(int limit);
}
