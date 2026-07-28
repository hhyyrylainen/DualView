using System.Net;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using DualView.Shared.Models;
using DualView.Shared.Models.DTO;
using DualView.Shared.Models.Enums;
using DualView.Shared.Requests;

namespace DualView.Shared.Services;

/// <summary>
///   Over HTTP calls to the database (from the clients).
/// </summary>
public abstract class HttpDatabaseAccessBase : IClientDatabaseService
{
    protected readonly HttpClient HttpClient;

    protected HttpDatabaseAccessBase(HttpClient httpClient)
    {
        HttpClient = httpClient;
    }

    public async Task<List<DualViewSettings>> GetDualViewSettingsAsync()
    {
        return await HttpClient.GetFromJsonAsync<List<DualViewSettings>>("api/v1/runnerSettings") ??
               new List<DualViewSettings>();
    }

    public async Task<DualViewSettings?> GetDualViewSettingsAsync(int id)
    {
        return await HttpClient.GetFromJsonAsync<DualViewSettings>($"api/v1/runnerSettings/{id}");
    }

    public async Task SaveDualViewSettingsAsync(DualViewSettings settings)
    {
        var response = await HttpClient.PutAsJsonAsync($"api/v1/runnerSettings/{settings.Id}", settings);
        response.EnsureSuccessStatusCode();
    }

    public async Task AddNewDualViewSettingsAsync(DualViewSettings settings)
    {
        var response = await HttpClient.PostAsJsonAsync("api/v1/runnerSettings", settings);
        response.EnsureSuccessStatusCode();

        var id = int.Parse(await response.Content.ReadAsStringAsync());
        settings.Id = id;
    }

    public async Task<DualViewSettings> GetAppSettingsAsync()
    {
        return await HttpClient.GetFromJsonAsync<DualViewSettings>("api/v1/settings") ?? new DualViewSettings();
    }

    public async Task SaveAppSettingsAsync(DualViewSettings settings)
    {
        var response = await HttpClient.PutAsJsonAsync("api/v1/settings", settings);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<MediaStorageFolderInfo>> GetMediaFoldersAsync(long? limitToParent = null)
    {
        if (limitToParent.HasValue)
        {
            return await HttpClient.GetFromJsonAsync<List<MediaStorageFolderInfo>>(
                       $"api/v1/mediaFolder?parentFolderId={limitToParent}") ??
                   throw new Exception("Failed to get folders");
        }

        return await HttpClient.GetFromJsonAsync<List<MediaStorageFolderInfo>>("api/v1/mediaFolder") ??
               throw new Exception("Failed to get folders");
    }

    public async Task<MediaStorageFolderDTO?> GetMediaFolderAsync(long id)
    {
        return await HttpClient.GetFromJsonAsync<MediaStorageFolderDTO>($"api/v1/mediaFolder/{id}");
    }

    public async Task<MediaStorageFolderDTO?> GetMediaFolderFromPathAsync(string path)
    {
        return await HttpClient.GetFromJsonAsync<MediaStorageFolderDTO?>("api/v1/mediaFolder/atPath?path=" +
                                                                         UrlEncoder.Default.Encode(path));
    }

    public async Task<Tuple<List<ConfiguredMediaInfo>, int>> GetMediaFolderContents(long folderId, int itemPage,
        int pageSize, FolderSortColumn sortColumn,
        SortDirection sortDirection)
    {
        return await HttpClient.GetFromJsonAsync<Tuple<List<ConfiguredMediaInfo>, int>>(
                   $"api/v1/mediaFolder/folder/{folderId}?page={itemPage}&pageSize={pageSize}&sortColumn={sortColumn}&sortDirection={sortDirection}") ??
               throw new Exception("Failed to get media");
    }

    public async Task<ConfiguredMediaDTO?> GetConfiguredMediaAsync(long mediaConfigId)
    {
        return await HttpClient.GetFromJsonAsync<ConfiguredMediaDTO?>($"api/v1/media/{mediaConfigId}");
    }

    public async Task<MediaConfigFolderInfo> GetConfiguredMediaFoldersAsync(long mediaConfigId)
    {
        return await HttpClient.GetFromJsonAsync<MediaConfigFolderInfo>($"api/v1/media/{mediaConfigId}/inFolders") ??
               throw new Exception("Failed to get media folders");
    }

    public async Task<List<ConfiguredMediaDTO>> GetConfiguredMediaSiblingsAsync(long mediaConfigId)
    {
        return await HttpClient.GetFromJsonAsync<List<ConfiguredMediaDTO>>($"api/v1/media/{mediaConfigId}/siblings") ??
               new List<ConfiguredMediaDTO>();
    }

    public async Task<ConfiguredMediaDTO> CreateConfiguredMediaAsync(long mediaId, string configName,
        List<string> folders)
    {
        var response = await HttpClient.PostAsJsonAsync("api/v1/media/createConfig",
            new CreateMediaConfigRequest(configName, mediaId)
            {
                FoldersToAdd = folders,
                CreateFolders = true,
            });

        response.EnsureSuccessStatusCode();

        // Decode the response as JSON
        return await response.Content.ReadFromJsonAsync<ConfiguredMediaDTO>() ??
               throw new Exception("Failed to read media config create response");
    }

    public async Task SaveConfiguredMediaAsync(ConfiguredMediaDTO media)
    {
        var response = await HttpClient.PutAsJsonAsync($"api/v1/media/{media.Id}", media);
        response.EnsureSuccessStatusCode();
    }

    public async Task<ConfiguredMediaDTO?> GetPrimeConfiguredMediaFromFileAsync(long mediaFileId)
    {
        return await HttpClient.GetFromJsonAsync<ConfiguredMediaDTO>($"api/v1/media/byMediaFileId/{mediaFileId}");
    }

    public async Task<bool> SetMediaKeepStatusAsync(long configuredMediaId, bool keep)
    {
        var response = await HttpClient.PostAsync($"api/v1/media/{configuredMediaId}/keepStatus?keep={keep}", null);
        response.EnsureSuccessStatusCode();

        return response.StatusCode == HttpStatusCode.Created;
    }

    public async Task<bool> IsMediaSafeToDeleteAsync(long configuredMediaId)
    {
        var response = await HttpClient.GetAsync($"api/v1/media/{configuredMediaId}/safeToDelete");
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<bool>();
    }

    public async Task DeleteMediaAsync(long configuredMediaId)
    {
        var response = await HttpClient.DeleteAsync($"api/v1/media/{configuredMediaId}");
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveMediaFromFolder(long mediaConfigurationId, string folderPath)
    {
        var response = await HttpClient.PostAsync(
            $"api/v1/mediaFolder/removeMediaFromFolder?folderPath={UrlEncoder.Default.Encode(folderPath)}&mediaConfigurationId={mediaConfigurationId}",
            null);
        response.EnsureSuccessStatusCode();
    }

    public async Task<long> CreateMediaFolder(string folderName, long? parentId)
    {
        var response = await HttpClient.PostAsJsonAsync("api/v1/mediaFolder",
            new CreateFolderRequest(folderName) { ParentFolderId = parentId });
        response.EnsureSuccessStatusCode();
        var idString = await response.Content.ReadAsStringAsync();
        return long.Parse(idString);
    }

    public async Task AddMediaToFolder(long mediaConfigurationId, string folderPath, bool canCreateRootFolder = false)
    {
        var response = await HttpClient.PostAsJsonAsync(
            $"api/v1/mediaFolder/addMediaToFolder?folderPath={UrlEncoder.Default.Encode(folderPath)}&" +
            $"mediaConfigurationId={mediaConfigurationId}&canCreateRootFolder={canCreateRootFolder}",
            new StringContent(string.Empty));
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<ConfiguredMediaDTO>> GetDeletedMediaAsync(int limit)
    {
        return await HttpClient.GetFromJsonAsync<List<ConfiguredMediaDTO>>($"api/v1/media/deleted?limit={limit}") ??
               new List<ConfiguredMediaDTO>();
    }

    public async Task RestoreMediaAsync(long configuredMediaId)
    {
        var response = await HttpClient.PostAsync($"api/v1/media/{configuredMediaId}/restore", null);
        response.EnsureSuccessStatusCode();
    }
}
