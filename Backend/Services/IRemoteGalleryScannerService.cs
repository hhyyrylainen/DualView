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

    public Task PauseGalleryAsync(long galleryId, CancellationToken cancellationToken);

    /// <summary>
    ///   Resumes scanning, optionally removing the first pending page before continuing.
    /// </summary>
    public Task ResumeGalleryAsync(long galleryId, CancellationToken cancellationToken, bool skipCurrent = false);

    /// <summary>
    ///   Deletes a gallery and prevents queued or in-flight results from being saved to it.
    /// </summary>
    public Task DeleteGalleryAsync(long galleryId, CancellationToken cancellationToken);

    /// <summary>
    ///   Validates and queues a gallery acceptance for background processing.
    /// </summary>
    public Task AcceptGalleryAsync(long galleryId, string name, IReadOnlyCollection<long>? mediaIds,
        CancellationToken cancellationToken);

    /// <summary>
    ///   Stops accepting new work and lets the active page finish within the supplied timeout.
    /// </summary>
    public Task StopAsync(TimeSpan timeout);
}
