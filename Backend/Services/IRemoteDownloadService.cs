using DualView.Shared.Models;

namespace Backend.Services;

/// <summary>
///   Queues media downloads received from remote browser scanners.
/// </summary>
public interface IRemoteDownloadService
{
    /// <summary>
    ///   Starts processing queued downloads.
    /// </summary>
    public void Start();

    /// <summary>
    ///   Stops processing queued downloads.
    /// </summary>
    /// <param name="wait">Whether to wait for the worker to finish.</param>
    /// <param name="timeout">The maximum time to wait.</param>
    public void Stop(bool wait, TimeSpan timeout);

    /// <summary>
    ///   Queues a remote download for serialized processing.
    /// </summary>
    /// <param name="failureCallback">Called after all download attempts fail.</param>
    public ValueTask QueueDownloadAsync(RemoteDownloadRequest request, CancellationToken cancellationToken,
        Func<Exception, CancellationToken, Task>? failureCallback = null);
}
