using Backend.Models;
using DualView.Shared.Models;

namespace Backend.Services;

/// <summary>
///   Records remote scan activity and provides a hook to enrich remote downloads.
/// </summary>
public interface IRemoteScanService
{
    /// <summary>
    ///   Enriches a download request before it is downloaded.
    /// </summary>
    public Task<RemoteDownloadRequest> EnrichDownloadAsync(RemoteDownloadRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    ///   Records a remote scan/download event.
    /// </summary>
    public Task RecordEventAsync(RemoteScanEvent scanEvent, CancellationToken cancellationToken);

    /// <summary>
    ///   Gets a snapshot of the recorded events.
    /// </summary>
    public Task<IReadOnlyList<RemoteScanEvent>> GetEventsAsync(CancellationToken cancellationToken);
}
