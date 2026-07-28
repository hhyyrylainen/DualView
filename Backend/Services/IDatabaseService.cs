using DualView.Shared.Services;
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
    public Task<List<MediaStorageFolder>> GetMediaFoldersAsync(long? limitToParent = null);
    public Task<MediaStorageFolder?> GetMediaFolderAsync(long id);
    public Task<MediaStorageFolder?> GetMediaFolderAsync(string name, long? parentFolderId);

    public Task<MediaStorageFolder> CreateMediaFolderAsync(string folderName, long? parentId);
    public Task<MediaStorageFolder?> GetMediaFolderFromPathAsync(string path);

    public Task<List<ConfiguredMedia>> GetMediaInFolderAsync(long folderId);
    public Task<ConfiguredMedia?> GetMediaByNameAndPath(string requestName, string mainFolder);

    // Media
    public Task<MediaFile?> GetMediaByHashAsync(string sha3);
    public Task<MediaFile?> GetMediaByIdAsync(long id);

    public Task SaveMediaFileAsync(MediaFile mediaFile);
    public Task<List<ConfiguredMedia>> GetMediaConfigurationsAsync(long mediaFileId);

    // NOTE: this uses the media ID and not the config ID
    public Task<ConfiguredMedia> GetConfiguredMediaPrimeAsync(long mediaId);

    public Task<ConfiguredMedia?> GetConfiguredMediaAsync(long id, bool loadMedia = false);
    public Task<ConfiguredMedia?> GetConfiguredMediaByMediaFileAsync(long id, bool loadMedia = false);

    public Task<ConfiguredMedia> CreateMediaConfig(MediaFile originalMedia, string newName, string mainFolder,
        bool markAsKeep, bool allowCreateFolder = true, bool allowRootFolderCreate = false);

    public Task MakeSureMediaIsSetToKeep(long configuredMediaId);

    public Task<List<ConfiguredMedia>> GetDeletedMediaAsync(int limit);

    /// <summary>
    ///   Creates a new media file and its accompanying prime config
    /// </summary>
    public Task<ConfiguredMedia> CreateMediaAsync(MediaFile mediaItem, string initialFolder,
        bool canCreateRootFolder = false);

    public Task SaveMediaConfigAsync(ConfiguredMedia media);
    public Task<List<ConfiguredMedia>> GetOldDeletedConfigsAsync(DateTime cutoff);
    public Task PurgeConfiguredMediaAsync(ConfiguredMedia config);
    public Task<List<MediaFile>> GetEligibleMediaFilesForPurgeAsync();
    public Task PurgeMediaFileAsync(MediaFile mediaFile);

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
}
