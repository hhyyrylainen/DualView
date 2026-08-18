using Backend.Models;
using DualView.Shared.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backend.Services;

/// <summary>
///   Coordinates remote scan enrichment and stores remote processing events.
/// </summary>
public sealed class RemoteScanService : IRemoteScanService
{
    private readonly IServiceScopeFactory serviceScopeFactory;
    private readonly ILogger<RemoteScanService> logger;
    private readonly SemaphoreSlim eventLock = new(1, 1);
    private readonly List<RemoteScanEvent> events = new();

    public RemoteScanService(IServiceScopeFactory serviceScopeFactory, ILogger<RemoteScanService> logger)
    {
        this.serviceScopeFactory = serviceScopeFactory;
        this.logger = logger;
    }

    public async Task<RemoteDownloadRequest> EnrichDownloadAsync(RemoteDownloadRequest request,
        CancellationToken cancellationToken)
    {
        // This is intentionally a no-op hook for now. A future scanner can use a fresh scope here
        // to load site-specific services, fetch the referrer, add tags, or create a download gallery.
        using var scope = serviceScopeFactory.CreateScope();
        await RecordEventInternalAsync(new RemoteScanEvent
        {
            EventType = "download-enrichment-requested",
            ImageUrl = request.ImageUrl,

            // TODO: split remote scan events into "internal" and user-readable ones, and set a duration after which
            // they get cleared
        }, cancellationToken);
        return request;
    }

    public async Task RecordEventAsync(RemoteScanEvent scanEvent, CancellationToken cancellationToken)
    {
        await RecordEventInternalAsync(scanEvent, cancellationToken);
    }

    // TODO: add filter parameter for user readable or all, and whether to clear user-readable events or not. These features will allow implementing a GUI.
    public async Task<IReadOnlyList<RemoteScanEvent>> GetEventsAsync(CancellationToken cancellationToken)
    {
        await eventLock.WaitAsync(cancellationToken);
        try
        {
            return events.ToList();
        }
        finally
        {
            eventLock.Release();
        }
    }

    private async Task RecordEventInternalAsync(RemoteScanEvent scanEvent, CancellationToken cancellationToken)
    {
        await eventLock.WaitAsync(cancellationToken);
        try
        {
            events.Add(scanEvent);
            logger.LogInformation("Remote scan event {EventType} for {ImageUrl}: {Detail}",
                scanEvent.EventType, scanEvent.ImageUrl, scanEvent.Detail);
        }
        finally
        {
            eventLock.Release();
        }
    }
}
