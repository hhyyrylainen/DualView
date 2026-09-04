using DualView.Shared.Models;

namespace Backend.Services;

/// <summary>
///   Creates and serially scans persisted remote galleries.
/// </summary>
public interface IRemoteGalleryScannerService
{
    /// <summary>
    ///   Validates and creates a gallery scan. The returned ID can be used by clients to display progress.
    /// </summary>
    public Task<long> CreateGalleryAsync(RemoteDownloadRequest request, CancellationToken cancellationToken);

    /// <summary>
    ///   Re-enqueues a gallery that stopped after an exhausted page retry budget.
    /// </summary>
    public Task RetryGalleryAsync(long galleryId, CancellationToken cancellationToken);
}
