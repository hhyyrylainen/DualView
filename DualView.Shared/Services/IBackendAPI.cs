using DualView.Shared.Models;
using DualView.Shared.Models.DTO;

namespace DualView.Shared.Services;

/// <summary>
///   Client access to all the backend APIs that aren't covered by the database interface.
/// </summary>
public interface IBackendAPI
{
    public Task<MediaFileDTO> ImportMedia(string fileName, Stream data, string? sectionName);

    // Operation operations
    public Task<OperationStatusUpdate> GetOperationStatus(long operationId);
    public Task<bool> PauseOperation(long operationId);
    public Task<bool> ResumeOperation(long operationId);
    public Task<bool> CancelOperation(long operationId);

    // Maintenance
    public Task<long> StartImageExistCheck();
    public Task<long> StartDeleteThumbnails();
    public Task<long> StartPurgeIncorrectlyDeleted();
    public Task<long> StartFixOrphanedResources();
}
