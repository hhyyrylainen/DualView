using System.Net.Http.Json;
using System.Text.Encodings.Web;
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

    public async Task<MediaFileDTO> ImportMedia(string fileName, Stream data, long targetCollectionId,
        bool importAlphaAsMask = false)
    {
        var response = await HttpClient.PostAsync(
            $"api/v1/media/import?targetCollectionId={targetCollectionId}&" +
            $"importAlphaAsMask={(importAlphaAsMask ? "true" : "false")}",
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
