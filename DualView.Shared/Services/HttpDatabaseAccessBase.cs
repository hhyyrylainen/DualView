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
        CollectionSortColumn sortColumn, SortDirection sortDirection, string? search = null)
    {
        var url =
            $"api/v1/collection/{collectionId}/contents?page={page}&pageSize={pageSize}&sortColumn={sortColumn}&sortDirection={sortDirection}";

        if (!string.IsNullOrEmpty(search))
        {
            url += $"&search={UrlEncoder.Default.Encode(search)}";
        }

        return await HttpClient.GetFromJsonAsync<Tuple<List<MediaFileDTO>, int>>(url) ??
               throw new Exception("Failed to get media");
    }

    public async Task<List<MediaFileDTO>> GetCollectionContents(long collectionId)
    {
        return await HttpClient.GetFromJsonAsync<List<MediaFileDTO>>($"api/v1/collection/{collectionId}/allContents") ??
               new List<MediaFileDTO>();
    }

    public async Task<MediaFileDTO?> GetMediaFileAsync(long mediaId)
    {
        return await HttpClient.GetFromJsonAsync<MediaFileDTO?>($"api/v1/media/{mediaId}");
    }

    public async Task<CollectionDTO?> GetCollectionAsync(long collectionId)
    {
        return await HttpClient.GetFromJsonAsync<CollectionDTO?>($"api/v1/collection/{collectionId}");
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

    public async Task<List<FolderPathDTO>> GetCollectionFolderPaths(long collectionId)
    {
        return await HttpClient.GetFromJsonAsync<List<FolderPathDTO>>(
                   $"api/v1/collection/{collectionId}/folderPaths") ??
               new List<FolderPathDTO>();
    }

    public async Task<List<FolderPathDTO>> GetFolderParentFolderPaths(long folderId)
    {
        return await HttpClient.GetFromJsonAsync<List<FolderPathDTO>>(
                   $"api/v1/mediaFolder/{folderId}/parentFolderPaths") ??
               new List<FolderPathDTO>();
    }

    public async Task<string> GetMediaFolderPath(long folderId)
    {
        return await HttpClient.GetStringAsync($"api/v1/mediaFolder/{folderId}/path");
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

    public async Task<long> CreateMediaFolder(string folderName, long parentId)
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

    public async Task AddCollectionToFolder(long collectionId, long folderId)
    {
        var response = await HttpClient.PostAsync($"api/v1/collection/{collectionId}/addToFolder/{folderId}", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task AddFolderToFolder(long folderId, long parentFolderId)
    {
        var response = await HttpClient.PostAsync($"api/v1/mediaFolder/{folderId}/addToFolder/{parentFolderId}", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveCollectionFromFolder(long collectionId, long folderId)
    {
        var response = await HttpClient.DeleteAsync($"api/v1/collection/{collectionId}/removeFromFolder/{folderId}");
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveFolderFromFolder(long folderId, long parentFolderId)
    {
        var response = await HttpClient.DeleteAsync($"api/v1/mediaFolder/{folderId}/removeFromFolder/{parentFolderId}");
        response.EnsureSuccessStatusCode();
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

    public async Task ReorderCollection(long collectionId, List<long> newImageOrderIds)
    {
        var response = await HttpClient.PostAsJsonAsync($"api/v1/collection/{collectionId}/reorder", newImageOrderIds);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<MediaFileDTO>> GetDeletedMediaAsync(int limit)
    {
        return await HttpClient.GetFromJsonAsync<List<MediaFileDTO>>($"api/v1/media/deleted?limit={limit}") ??
               new List<MediaFileDTO>();
    }

    public async Task<List<MediaFolderDTO>> GetDeletedMediaFoldersAsync(int limit)
    {
        return await HttpClient.GetFromJsonAsync<List<MediaFolderDTO>>($"api/v1/mediaFolder/deleted?limit={limit}") ??
               new List<MediaFolderDTO>();
    }

    public async Task<List<CollectionDTO>> GetDeletedCollectionsAsync(int limit)
    {
        return await HttpClient.GetFromJsonAsync<List<CollectionDTO>>($"api/v1/collection/deleted?limit={limit}") ??
               new List<CollectionDTO>();
    }

    public async Task RestoreMediaAsync(long mediaId)
    {
        var response = await HttpClient.PostAsync($"api/v1/media/{mediaId}/restore", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task PurgeMediaAsync(long mediaId)
    {
        var response = await HttpClient.PostAsync($"api/v1/media/{mediaId}/purge", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteMediaFolderAsync(long folderId)
    {
        var response = await HttpClient.DeleteAsync($"api/v1/mediaFolder/{folderId}");
        response.EnsureSuccessStatusCode();
    }

    public async Task RestoreMediaFolderAsync(long folderId)
    {
        var response = await HttpClient.PostAsync($"api/v1/mediaFolder/{folderId}/restore", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task PurgeMediaFolderAsync(long folderId)
    {
        var response = await HttpClient.PostAsync($"api/v1/mediaFolder/{folderId}/purge", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteCollectionAsync(long collectionId)
    {
        var response = await HttpClient.DeleteAsync($"api/v1/collection/{collectionId}");
        response.EnsureSuccessStatusCode();
    }

    public async Task RestoreCollectionAsync(long collectionId)
    {
        var response = await HttpClient.PostAsync($"api/v1/collection/{collectionId}/restore", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task PurgeCollectionAsync(long collectionId)
    {
        var response = await HttpClient.PostAsync($"api/v1/collection/{collectionId}/purge", null);
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
        int pageSize, FolderSortColumn sortColumn, SortDirection sortDirection, string? searchText = null)
    {
        var url =
            $"api/v1/mediaFolder/{folderId}/contents?page={itemPage}&pageSize={pageSize}&sortColumn={sortColumn}&" +
            $"sortDirection={sortDirection}";

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            url += $"&searchText={Uri.EscapeDataString(searchText)}";
        }

        return await HttpClient.GetFromJsonAsync<Tuple<List<ConfiguredMediaInfo>, int>>(url) ??
               throw new Exception("Failed to get folder contents");
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

    public async Task<bool> SetMediaRatingAsync(long mediaId, bool isFavorited, int stars)
    {
        var response = await HttpClient.PostAsJsonAsync($"api/v1/media/{mediaId}/rating", new { isFavorited, stars });
        return response.IsSuccessStatusCode;
    }

    public async Task AddMediaToUploadSectionAsync(long mediaId, long sectionId, int index)
    {
        var response = await HttpClient.PostAsync(
            $"api/v1/uploadSection/{sectionId}/addMedia?mediaId={mediaId}&index={index}", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task<int> GetNextUploadSectionIndexAsync(long sectionId)
    {
        return await HttpClient.GetFromJsonAsync<int>($"api/v1/uploadSection/{sectionId}/nextIndex");
    }

    public async Task SetMediaTemporaryStatusAsync(long mediaId, bool isTemporary)
    {
        var response =
            await HttpClient.PostAsync($"api/v1/media/{mediaId}/temporaryStatus?isTemporary={isTemporary}", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task BumpUploadSectionLastImportedAsync(long sectionId)
    {
        var response = await HttpClient.PostAsync($"api/v1/uploadSection/{sectionId}/bumpLastImported", null);
        response.EnsureSuccessStatusCode();
    }

    // Tags
    public async Task<long> CreateTagAsync(string name, TagCategory category)
    {
        var response =
            await HttpClient.PostAsJsonAsync("api/v1/tag", new CreateTagRequest { Name = name, Category = category });
        response.EnsureSuccessStatusCode();
        return long.Parse(await response.Content.ReadAsStringAsync());
    }

    public async Task UpdateTagAsync(long id, string? name, string? description, TagCategory? category,
        long? exampleMediaId)
    {
        var response = await HttpClient.PutAsJsonAsync($"api/v1/tag/{id}",
            new UpdateTagRequest
                { Name = name, Description = description, Category = category, ExampleMediaId = exampleMediaId });
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteTagAsync(long id)
    {
        var response = await HttpClient.DeleteAsync($"api/v1/tag/{id}");
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<TagDTO>> GetAllTagsAsync()
    {
        return await HttpClient.GetFromJsonAsync<List<TagDTO>>("api/v1/tag") ?? new List<TagDTO>();
    }

    public async Task<TagDTO?> GetTagAsync(long id)
    {
        return await HttpClient.GetFromJsonAsync<TagDTO?>($"api/v1/tag/{id}");
    }

    public async Task<List<TagDTO>> SearchTagsWildcardAsync(string search)
    {
        return await HttpClient.GetFromJsonAsync<List<TagDTO>>(
                   $"api/v1/tag/search?search={Uri.EscapeDataString(search)}") ??
               new List<TagDTO>();
    }

    public async Task<TagDTO?> GetTagByNameAsync(string name)
    {
        return await HttpClient.GetFromJsonAsync<TagDTO?>($"api/v1/tag/byName?name={Uri.EscapeDataString(name)}");
    }

    public async Task<long> CreateTagModifierAsync(string name)
    {
        var response =
            await HttpClient.PostAsJsonAsync("api/v1/tagModifier", new CreateModifierRequest { Name = name });
        response.EnsureSuccessStatusCode();
        return long.Parse(await response.Content.ReadAsStringAsync());
    }

    public async Task UpdateTagModifierAsync(long id, string? name, string? description)
    {
        var response = await HttpClient.PutAsJsonAsync($"api/v1/tagModifier/{id}",
            new UpdateModifierRequest { Name = name, Description = description });
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteTagModifierAsync(long id)
    {
        var response = await HttpClient.DeleteAsync($"api/v1/tagModifier/{id}");
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<string>> GetTagAliasesAsync(long tagId)
    {
        return await HttpClient.GetFromJsonAsync<List<string>>($"api/v1/tag/{tagId}/alias") ?? new List<string>();
    }

    public async Task<List<TagDTO>> GetTagImpliesAsync(long tagId)
    {
        return await HttpClient.GetFromJsonAsync<List<TagDTO>>($"api/v1/tag/{tagId}/imply") ?? new List<TagDTO>();
    }

    public async Task<List<TagModifierDTO>> GetAllTagModifiersAsync()
    {
        return await HttpClient.GetFromJsonAsync<List<TagModifierDTO>>("api/v1/tagModifier") ??
               new List<TagModifierDTO>();
    }

    public async Task<TagModifierDTO?> GetTagModifierAsync(long id)
    {
        return await HttpClient.GetFromJsonAsync<TagModifierDTO?>($"api/v1/tagModifier/{id}");
    }

    public async Task CreateTagAliasAsync(long tagId, string alias)
    {
        var response =
            await HttpClient.PostAsJsonAsync($"api/v1/tag/{tagId}/alias", new CreateTagAliasRequest { Alias = alias });
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteTagAliasAsync(long tagId, string alias)
    {
        var response =
            await HttpClient.DeleteAsync($"api/v1/tag/{tagId}/alias?alias={UrlEncoder.Default.Encode(alias)}");
        response.EnsureSuccessStatusCode();
    }

    public async Task AddTagImplicationAsync(long tagId, long impliedTagId)
    {
        var response = await HttpClient.PostAsJsonAsync($"api/v1/tag/{tagId}/imply",
            new AddImplicationRequest { ImpliedTagId = impliedTagId });
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveTagImplicationAsync(long tagId, long impliedTagId)
    {
        var response = await HttpClient.DeleteAsync($"api/v1/tag/{tagId}/imply/{impliedTagId}");
        response.EnsureSuccessStatusCode();
    }

    // Applied Tags
    public async Task<long> AddAppliedTagToMediaAsync(long mediaId, long tagId, List<long>? modifierIds,
        long? combinedWithAppliedTagId, string? combineWord)
    {
        var response = await HttpClient.PostAsJsonAsync($"api/v1/media/{mediaId}/appliedTag",
            new AddAppliedTagRequest
            {
                TagId = tagId, ModifierIds = modifierIds, CombinedWithAppliedTagId = combinedWithAppliedTagId,
                CombineWord = combineWord
            });
        response.EnsureSuccessStatusCode();
        return long.Parse(await response.Content.ReadAsStringAsync());
    }

    public async Task RemoveAppliedTagFromMediaAsync(long mediaId, long appliedTagId)
    {
        var response = await HttpClient.DeleteAsync($"api/v1/media/{mediaId}/appliedTag/{appliedTagId}");
        response.EnsureSuccessStatusCode();
    }

    public async Task<long> AddAppliedTagToCollectionAsync(long collectionId, long tagId, List<long>? modifierIds,
        long? combinedWithAppliedTagId, string? combineWord)
    {
        var response = await HttpClient.PostAsJsonAsync($"api/v1/collection/{collectionId}/appliedTag",
            new AddAppliedTagRequest
            {
                TagId = tagId, ModifierIds = modifierIds, CombinedWithAppliedTagId = combinedWithAppliedTagId,
                CombineWord = combineWord
            });
        response.EnsureSuccessStatusCode();
        return long.Parse(await response.Content.ReadAsStringAsync());
    }

    public async Task RemoveAppliedTagFromCollectionAsync(long collectionId, long appliedTagId)
    {
        var response = await HttpClient.DeleteAsync($"api/v1/collection/{collectionId}/appliedTag/{appliedTagId}");
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<AppliedTagDTO>> GetMediaAppliedTagsAsync(long mediaId)
    {
        return await HttpClient.GetFromJsonAsync<List<AppliedTagDTO>>($"api/v1/media/{mediaId}/appliedTag") ??
               new List<AppliedTagDTO>();
    }

    public async Task<List<AppliedTagDTO>> GetCollectionAppliedTagsAsync(long collectionId)
    {
        return await HttpClient.GetFromJsonAsync<List<AppliedTagDTO>>($"api/v1/collection/{collectionId}/appliedTag") ??
               new List<AppliedTagDTO>();
    }

    // Import & Galleries
    public async Task<MediaImportInfoDTO?> GetMediaImportInfoAsync(long mediaId)
    {
        return await HttpClient.GetFromJsonAsync<MediaImportInfoDTO?>($"api/v1/media/{mediaId}/importInfo");
    }

    public async Task<long> CreateDownloadGalleryAsync(string galleryUrl)
    {
        var response = await HttpClient.PostAsJsonAsync("api/v1/downloadGallery", new { galleryUrl });
        response.EnsureSuccessStatusCode();
        return long.Parse(await response.Content.ReadAsStringAsync());
    }

    public async Task UpdateDownloadGalleryAsync(long id, string? targetPath, string? galleryName, bool? isDownloaded,
        string? tagsString)
    {
        var response = await HttpClient.PutAsJsonAsync($"api/v1/downloadGallery/{id}",
            new { targetPath, galleryName, isDownloaded, tagsString });
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteDownloadGalleryAsync(long id)
    {
        var response = await HttpClient.DeleteAsync($"api/v1/downloadGallery/{id}");
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<DownloadGalleryDTO>> GetAllDownloadGalleriesAsync()
    {
        return await HttpClient.GetFromJsonAsync<List<DownloadGalleryDTO>>("api/v1/downloadGallery") ??
               new List<DownloadGalleryDTO>();
    }

    public async Task<DownloadGalleryDTO?> GetDownloadGalleryAsync(long id)
    {
        return await HttpClient.GetFromJsonAsync<DownloadGalleryDTO?>($"api/v1/downloadGallery/{id}");
    }
}
