using System.Net.Http.Json;
using DualView.Shared.Models;
using DualView.Shared.Models.DTO;

namespace DualView.Shared.Services;

public class HttpBackendAPI : IBackendAPI
{
    protected readonly HttpClient HttpClient;

    protected HttpBackendAPI(HttpClient httpClient)
    {
        HttpClient = httpClient;
    }

    public async Task<string> RegenerateBrowserPluginAccessKey()
    {
        var response = await HttpClient.PostAsync("api/v1/settings/regenerateBrowserPluginAccessKey", null);
        response.EnsureSuccessStatusCode();
        var accessKey = (await response.Content.ReadAsStringAsync()).Trim();
        return string.IsNullOrEmpty(accessKey)
            ? throw new InvalidOperationException("The server returned an empty browser plugin access key")
            : accessKey;
    }

    public async Task<MediaFileDTO> ImportMedia(string fileName, Stream data, string? sectionName,
        string? sourcePath = null)
    {
        var url = $"api/v1/media/import?sectionName={Uri.EscapeDataString(sectionName ?? "")}";

        if (!string.IsNullOrEmpty(sourcePath))
        {
            url += $"&sourcePath={Uri.EscapeDataString(sourcePath)}";
        }
        else
        {
            throw new ArgumentException("sourcePath cannot be null or empty");
        }

        var response = await HttpClient.PostAsync(
            url,
            new MultipartFormDataContent
            {
                { new StreamContent(data), "file", fileName }
            });

        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadFromJsonAsync<MediaFileDTO>() ??
                      throw new Exception("Failed to deserialize response");
        return content;
    }

    public async Task<OperationStatusUpdate> GetOperationStatus(long operationId)
    {
        return await HttpClient.GetFromJsonAsync<OperationStatusUpdate>($"api/v1/operation/{operationId}") ??
               new OperationStatusUpdate(-1, "Couldn't reach backend")
               {
                   Error = true,
               };
    }

    public async Task<bool> PauseOperation(long operationId)
    {
        var response = await HttpClient.PostAsync($"api/v1/operation/{operationId}/pause", null);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> ResumeOperation(long operationId)
    {
        var response = await HttpClient.PostAsync($"api/v1/operation/{operationId}/resume", null);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> CancelOperation(long operationId)
    {
        var response = await HttpClient.PostAsync($"api/v1/operation/{operationId}/cancel", null);
        return response.IsSuccessStatusCode;
    }

    public async Task ClearCurrentMissingTagDetections()
    {
        var response = await HttpClient.PostAsync("api/v1/missingTag/clearCurrent", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task<long> StartCollectionVisualSimilaritySort(long collectionId)
    {
        var response = await HttpClient.PostAsync($"api/v1/collection/{collectionId}/sortByVisualSimilarity", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<long>();
    }

    public async Task<long> StartCollectionVisualSimilaritySort(long collectionId, IReadOnlyList<long> selectedImageIds)
    {
        var response = await HttpClient.PostAsJsonAsync(
            $"api/v1/collection/{collectionId}/findMostSimilar", selectedImageIds);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<long>();
    }

    public async Task<long> StartImportSectionVisualSimilaritySort(long sectionId)
    {
        var response = await HttpClient.PostAsync($"api/v1/uploadSection/{sectionId}/sortByVisualSimilarity", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<long>();
    }

    public async Task<List<long>> GetCollectionVisualSimilarityOrder(long operationId)
    {
        return await HttpClient.GetFromJsonAsync<List<long>>(
                   $"api/v1/collection/sortByVisualSimilarity/{operationId}") ??
               new List<long>();
    }

    public async Task<long> StartImageExistCheck()
    {
        var response = await HttpClient.PostAsync("api/v1/maintenance/checkFiles", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<long>();
    }

    public async Task<long> StartDeleteThumbnails()
    {
        var response = await HttpClient.PostAsync("api/v1/maintenance/deleteThumbnails", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<long>();
    }

    public async Task<long> StartPurgeIncorrectlyDeleted()
    {
        var response = await HttpClient.PostAsync("api/v1/maintenance/purgeIncorrectlyDeleted", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<long>();
    }

    public async Task<long> StartFixOrphanedResources()
    {
        var response = await HttpClient.PostAsync("api/v1/maintenance/fixOrphaned", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<long>();
    }

    /*public async Task<long> StartLocalModelImport(RemoteModelType type, string targetFolder, string fileName,
    string modelName, int runnerId, Stream data, string? baseModel = null)
{
    using var content = new MultipartFormDataContent();
    content.Add(new StringContent(((int)type).ToString()), "type");
    content.Add(new StringContent(targetFolder), "targetFolder");
    content.Add(new StringContent(fileName), "fileName");
    content.Add(new StringContent(modelName), "modelName");
    content.Add(new StringContent(runnerId.ToString()), "runnerId");
    if (!string.IsNullOrWhiteSpace(baseModel))
        content.Add(new StringContent(baseModel), "baseModel");
    content.Add(new StreamContent(data), "file", fileName);

    var response = await HttpClient.PostAsync("api/v1/ModelImport/local", content);
    response.EnsureSuccessStatusCode();

    var id = await response.Content.ReadFromJsonAsync<long?>();
    if (id == null)
        throw new Exception("Failed to deserialize operation id");
    return id.Value;
}*/
}
