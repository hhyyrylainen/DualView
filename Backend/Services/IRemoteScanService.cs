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
    ///   Inspects a URL and returns information about it. Used to determine what to do with the URL.
    /// </summary>
    /// <param name="request">Request with the URL and other info to inspect</param>
    /// <param name="cancellationToken">Cancellation</param>
    /// <returns>Info on the URL. If this returns unknown, then nothing can be done</returns>
    public Task<UrlInformation> InspectUrlAsync(RemoteDownloadRequest request, CancellationToken cancellationToken);

    /// <summary>
    ///   Records a remote scan/download event.
    /// </summary>
    public Task RecordEventAsync(RemoteScanEvent scanEvent, CancellationToken cancellationToken);

    /// <summary>
    ///   Gets a snapshot of the recorded events.
    /// </summary>
    public Task<IReadOnlyList<RemoteScanEvent>> GetEventsAsync(CancellationToken cancellationToken);
}
