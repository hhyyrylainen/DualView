using DualView.Shared.Models;

namespace Backend.Services;

/// <summary>
///   Queues media downloads received from remote browser scanners.
/// </summary>
public interface IRemoteDownloadService
{
    /// <summary>
    ///   Queues a remote download for serialized processing.
    /// </summary>
    public ValueTask QueueDownloadAsync(RemoteDownloadRequest request, CancellationToken cancellationToken);
}
