using DualView.Shared.Models;
using DualView.Shared.Services;
using Backend.Services;
using Microsoft.AspNetCore.SignalR;

namespace DualView.Server.Services;

public class EntityUpdateNotifier : IEntityUpdateNotifier
{
    private readonly IHubContext<DataHub, IDataHub> hubContext;
    private readonly IHubContext<RealTimeDataHub, IRealTimeDataHub> realTimeHubContext;
    private readonly ILogger<EntityUpdateNotifier> logger;

    public EntityUpdateNotifier(IHubContext<DataHub, IDataHub> hubContext,
        IHubContext<RealTimeDataHub, IRealTimeDataHub> realTimeHubContext,
        ILogger<EntityUpdateNotifier> logger)
    {
        this.hubContext = hubContext;
        this.realTimeHubContext = realTimeHubContext;
        this.logger = logger;
    }

    public async Task NotifyAppSettingsUpdated()
    {
        logger.LogDebug("Broadcasting single settings update to all clients");
        await hubContext.Clients.All.AppSettingsUpdated();
    }

    public async Task NotifyMediaFoldersUpdated()
    {
        logger.LogDebug("Broadcasting media folders update to all clients");
        await hubContext.Clients.All.MediaFoldersUpdated();
        await realTimeHubContext.Clients.All.OnMediaFolderUpdated();
    }

    public async Task NotifyMediaFolderContentsUpdated(long folderId)
    {
        logger.LogDebug("Broadcasting media folder contents update for specific folder to all clients");
        await hubContext.Clients.All.MediaFolderContentsUpdated(folderId);
    }

    public async Task NotifyMediaUpdated(long id)
    {
        logger.LogDebug("Broadcasting media update to all clients");
        await hubContext.Clients.All.MediaUpdated(id);
        await realTimeHubContext.Clients.All.OnMediaUpdated(id);
    }

    public async Task OperationStatusUpdated(OperationStatusUpdate update)
    {
        await hubContext.Clients.All.OperationStatusUpdated(update);
    }

    public async Task NotifyCollectionUpdated(long id)
    {
        logger.LogDebug("Broadcasting single collection update to all clients");
        await hubContext.Clients.All.CollectionUpdated(id);
    }

    public async Task NotifyCollectionContentsUpdated(long id)
    {
        logger.LogDebug("Broadcasting single collection contents update to all clients");
        await hubContext.Clients.All.CollectionContentsUpdated(id);
    }

    public async Task NotifyTagUpdated(long id)
    {
        logger.LogDebug("Broadcasting single tag update to all clients");
        await hubContext.Clients.All.TagUpdated(id);
    }

    public async Task NotifyTagsUpdated()
    {
        logger.LogDebug("Broadcasting tags list update to all clients");
        await hubContext.Clients.All.TagsUpdated();
    }

    public async Task NotifyTagModifiersUpdated()
    {
        logger.LogDebug("Broadcasting tag modifiers list update to all clients");
        await hubContext.Clients.All.TagModifiersUpdated();
    }

    public async Task NotifyDownloadGalleriesUpdated()
    {
        logger.LogDebug("Broadcasting download galleries list update to all clients");
        await hubContext.Clients.All.DownloadGalleriesUpdated();
    }

    public async Task NotifyDownloadGalleryUpdated(long id)
    {
        logger.LogDebug("Broadcasting single download gallery update to all clients");
        await hubContext.Clients.All.DownloadGalleryUpdated(id);
    }

    public async Task NotifyUploadSectionActiveChanged(long? sectionId)
    {
        logger.LogDebug("Broadcasting active upload section update to all clients");
        await hubContext.Clients.All.UploadSectionActiveChanged(sectionId);
    }

    public async Task NotifyUploadSectionsUpdated()
    {
        logger.LogDebug("Broadcasting upload sections list update to all clients");
        await hubContext.Clients.All.UploadSectionsUpdated();
    }

    public async Task NotifyUploadSectionUpdated(long sectionId)
    {
        logger.LogDebug("Broadcasting upload section update to all clients");
        await hubContext.Clients.All.UploadSectionUpdated(sectionId);
    }

    public async Task NotifyUploadSectionContentsUpdated(long sectionId)
    {
        logger.LogDebug("Broadcasting upload section contents update to all clients");
        await hubContext.Clients.All.UploadSectionContentsUpdated(sectionId);
    }

    public async Task NotifyMissingTagsUpdated()
    {
        logger.LogDebug("Broadcasting missing tags update to all clients");
        await hubContext.Clients.All.MissingTagsUpdated();
    }
}
