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

    public async Task<List<MediaFolderInfo>> GetMediaFoldersAsync(long? limitToParent = null)
    {
        if (limitToParent.HasValue)
        {
            return await HttpClient.GetFromJsonAsync<List<MediaFolderInfo>>(
                       $"api/v1/mediaFolder?parentFolderId={limitToParent}") ??
                   throw new Exception("Failed to get folders");
        }

        return await HttpClient.GetFromJsonAsync<List<MediaFolderInfo>>("api/v1/mediaFolder") ??
               throw new Exception("Failed to get folders");
    }

    public async Task<MediaFolderDTO?> GetMediaFolderAsync(long id)
    {
        return await HttpClient.GetFromJsonAsync<MediaFolderDTO>($"api/v1/mediaFolder/{id}");
    }

    public async Task<MediaFolderDTO?> GetMediaFolderFromPathAsync(string path)
    {
        return await HttpClient.GetFromJsonAsync<MediaFolderDTO?>("api/v1/mediaFolder/atPath?path=" +
                                                                         UrlEncoder.Default.Encode(path));
    }

    public async Task<Tuple<List<CollectionDTO>, int>> GetFolderCollections(long folderId, int page, int pageSize)
    {
        return await HttpClient.GetFromJsonAsync<Tuple<List<CollectionDTO>, int>>(
                   $"api/v1/mediaFolder/{folderId}/collections?page={page}&pageSize={pageSize}") ??
               throw new Exception("Failed to get collections");
    }

    public async Task<Tuple<List<MediaFileDTO>, int>> GetCollectionContents(long collectionId, int page, int pageSize,
        FolderSortColumn sortColumn, SortDirection sortDirection)
    {
        return await HttpClient.GetFromJsonAsync<Tuple<List<MediaFileDTO>, int>>(
                   $"api/v1/collection/{collectionId}/contents?page={page}&pageSize={pageSize}&sortColumn={sortColumn}&sortDirection={sortDirection}") ??
               throw new Exception("Failed to get media");
    }

    public async Task<MediaFileDTO?> GetMediaFileAsync(long mediaId)
    {
        return await HttpClient.GetFromJsonAsync<MediaFileDTO?>($"api/v1/media/{mediaId}");
    }

    public async Task<List<long>> GetMediaCollectionsAsync(long mediaId)
    {
        return await HttpClient.GetFromJsonAsync<List<long>>($"api/v1/media/{mediaId}/collections") ??
               new List<long>();
    }

    public async Task<List<MediaFileDTO>> GetMediaFileSiblingsAsync(long mediaId)
    {
        return await HttpClient.GetFromJsonAsync<List<MediaFileDTO>>($"api/v1/media/{mediaId}/siblings") ??
               new List<MediaFileDTO>();
    }

    public async Task<MediaFileDTO> CreateMediaFileAsync(MediaFileDTO mediaFile, long collectionId)
    {
        var response = await HttpClient.PostAsJsonAsync($"api/v1/media?collectionId={collectionId}", mediaFile);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<MediaFileDTO>() ??
               throw new Exception("Failed to read media create response");
    }

    public async Task SaveMediaFileAsync(MediaFileDTO media)
    {
        var response = await HttpClient.PutAsJsonAsync($"api/v1/media/{media.Id}", media);
        response.EnsureSuccessStatusCode();
    }

    public async Task<bool> SetMediaKeepStatusAsync(long mediaId, bool keep)
    {
        var response = await HttpClient.PostAsync($"api/v1/media/{mediaId}/keepStatus?keep={keep}", null);
        response.EnsureSuccessStatusCode();

        return response.StatusCode == HttpStatusCode.Created;
    }

    public async Task<bool> IsMediaSafeToDeleteAsync(long mediaId)
    {
        var response = await HttpClient.GetAsync($"api/v1/media/{mediaId}/safeToDelete");
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<bool>();
    }

    public async Task DeleteMediaAsync(long mediaId)
    {
        var response = await HttpClient.DeleteAsync($"api/v1/media/{mediaId}");
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

    public async Task<long> CreateCollection(string collectionName, long folderId)
    {
        var response = await HttpClient.PostAsJsonAsync($"api/v1/mediaFolder/{folderId}/collections", collectionName);
        response.EnsureSuccessStatusCode();
        var idString = await response.Content.ReadAsStringAsync();
        return long.Parse(idString);
    }

    public async Task AddMediaToCollection(long mediaId, long collectionId, int sequenceNumber)
    {
        var response = await HttpClient.PostAsync(
            $"api/v1/collection/{collectionId}/addMedia?mediaId={mediaId}&sequenceNumber={sequenceNumber}",
            null);
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveMediaFromCollection(long mediaId, long collectionId)
    {
        var response = await HttpClient.PostAsync(
            $"api/v1/collection/{collectionId}/removeMedia?mediaId={mediaId}",
            null);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<MediaFileDTO>> GetDeletedMediaAsync(int limit)
    {
        return await HttpClient.GetFromJsonAsync<List<MediaFileDTO>>($"api/v1/media/deleted?limit={limit}") ??
               new List<MediaFileDTO>();
    }

    public async Task RestoreMediaAsync(long mediaId)
    {
        var response = await HttpClient.PostAsync($"api/v1/media/{mediaId}/restore", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task<ConfiguredMediaDTO?> GetConfiguredMediaAsync(long mediaConfigId)
    {
        var media = await GetMediaFileAsync(mediaConfigId);
        return media != null ? new ConfiguredMediaDTO(media) : null;
    }

    public Task<MediaConfigFolderInfo> GetConfiguredMediaFoldersAsync(long mediaConfigId)
    {
        return Task.FromResult(new MediaConfigFolderInfo("Media", "Root"));
    }

    public async Task<List<ConfiguredMediaDTO>> GetConfiguredMediaSiblingsAsync(long mediaConfigId)
    {
        var siblings = await GetMediaFileSiblingsAsync(mediaConfigId);
        return siblings.Select(s => new ConfiguredMediaDTO(s)).ToList();
    }

    public async Task<Tuple<List<ConfiguredMediaInfo>, int>> GetMediaFolderContents(long folderId, int itemPage,
        int pageSize, FolderSortColumn sortColumn, SortDirection sortDirection)
    {
        // This is tricky, maybe just return empty or error
        return new Tuple<List<ConfiguredMediaInfo>, int>(new List<ConfiguredMediaInfo>(), 0);
    }

    public Task AddMediaToFolder(long mediaConfigurationId, string folderPath, bool canCreateRootFolder = false)
    {
        return Task.CompletedTask;
    }

    public Task RemoveMediaFromFolder(long mediaConfigurationId, string folderPath)
    {
        return Task.CompletedTask;
    }

    public Task<ConfiguredMediaDTO> CreateConfiguredMediaAsync(long mediaId, string configName, List<string> folders)
    {
        throw new NotSupportedException();
    }

    public Task SaveConfiguredMediaAsync(ConfiguredMediaDTO media)
    {
        return SaveMediaFileAsync(media);
    }

    public async Task<ConfiguredMediaDTO?> GetPrimeConfiguredMediaFromFileAsync(long mediaFileId)
    {
        var media = await GetMediaFileAsync(mediaFileId);
        return media != null ? new ConfiguredMediaDTO(media) : null;
    }
}
