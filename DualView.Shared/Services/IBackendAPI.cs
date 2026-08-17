using DualView.Shared.Models;
using DualView.Shared.Models.DTO;

namespace DualView.Shared.Services;

/// <summary>
///   Client access to all the backend APIs that aren't covered by the database interface.
/// </summary>
public interface IBackendAPI
{
    public Task<string> RegenerateBrowserPluginAccessKey();

    public Task<MediaFileDTO> ImportMedia(string fileName, Stream data, string? sectionName,
        string? sourcePath = null);

    // Operation operations
    public Task<OperationStatusUpdate> GetOperationStatus(long operationId);
    public Task<bool> PauseOperation(long operationId);
    public Task<bool> ResumeOperation(long operationId);
    public Task<bool> CancelOperation(long operationId);
    public Task<long> StartCollectionVisualSimilaritySort(long collectionId);
    public Task<long> StartImportSectionVisualSimilaritySort(long sectionId);
    public Task<List<long>> GetCollectionVisualSimilarityOrder(long operationId);

    // Maintenance
    public Task<long> StartImageExistCheck();
    public Task<long> StartDeleteThumbnails();
    public Task<long> StartPurgeIncorrectlyDeleted();
    public Task<long> StartFixOrphanedResources();
}
