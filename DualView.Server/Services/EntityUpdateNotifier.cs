using DualView.Shared.Models;
using DualView.Shared.Services;
using Backend.Services;
using Microsoft.AspNetCore.SignalR;

namespace DualView.Server.Services;

public class EntityUpdateNotifier : IEntityUpdateNotifier
{
    private readonly IHubContext<DataHub, IDataHub> hubContext;
    private readonly ILogger<EntityUpdateNotifier> logger;

    public EntityUpdateNotifier(IHubContext<DataHub, IDataHub> hubContext, ILogger<EntityUpdateNotifier> logger)
    {
        this.hubContext = hubContext;
        this.logger = logger;
    }

    public async Task NotifyDualViewSettingsListUpdated()
    {
        logger.LogDebug("Broadcasting settings update to all clients");
        await hubContext.Clients.All.DualViewSettingsListUpdated();
    }

    public async Task NotifyDualViewSettingsUpdated(int id)
    {
        logger.LogDebug("Broadcasting single settings update to all clients");
        await hubContext.Clients.All.DualViewSettingsUpdated(id);
    }

    public async Task NotifyAppSettingsUpdated()
    {
        logger.LogDebug("Broadcasting single settings update to all clients");
        await hubContext.Clients.All.AppSettingsUpdated();
    }

    public async Task NotifyChatsListUpdated()
    {
        logger.LogDebug("Broadcasting chats update to all clients");
        await hubContext.Clients.All.ChatsListUpdated();
    }

    public async Task NotifyChatUpdated(long id)
    {
        logger.LogDebug("Broadcasting single chat update to all clients");
        await hubContext.Clients.All.ChatUpdated(id);
    }

    public async Task ChatMessagesUpdated(long id)
    {
        logger.LogDebug("Broadcasting single chat new message added to all clients");
        await hubContext.Clients.All.ChatMessagesUpdated(id);
    }

    public async Task NotifyChatMessageUpdated(long id, int index)
    {
        logger.LogDebug("Broadcasting chat message update to all clients");
        await hubContext.Clients.All.ChatMessageUpdated(id, index);
    }

    public async Task NotifyChatMessagePrimaryUpdated(long id, int index, int generation)
    {
        logger.LogDebug("Broadcasting chat message generation primary changed to all clients");
        await hubContext.Clients.All.ChatMessagePrimaryUpdated(id, index, generation);
    }

    public async Task NotifyWorldsListUpdated()
    {
        logger.LogDebug("Broadcasting world creation to all clients");
        await hubContext.Clients.All.WorldsListUpdated();
    }

    public async Task NotifyWorldUpdated(long id)
    {
        logger.LogDebug("Broadcasting world update to all clients");
        await hubContext.Clients.All.WorldUpdated(id);
    }

    public async Task NotifyWorldChatsListUpdated(long worldId)
    {
        logger.LogDebug("Broadcasting world chats list update to all clients");
        await hubContext.Clients.All.WorldChatsUpdated(worldId);
    }

    public async Task NotifyWorkflowFoldersUpdated()
    {
        logger.LogDebug("Broadcasting workflow folders update to all clients");
        await hubContext.Clients.All.WorkflowFoldersUpdated();
    }

    public async Task NotifyWorkflowListUpdated()
    {
        logger.LogDebug("Broadcasting workflow list update to all clients");
        await hubContext.Clients.All.WorkflowListUpdated();
    }

    public async Task NotifyWorkflowUpdated(long id)
    {
        logger.LogDebug("Broadcasting single workflow update to all clients");
        await hubContext.Clients.All.WorkflowUpdated(id);
    }

    public async Task NotifyMediaFoldersUpdated()
    {
        logger.LogDebug("Broadcasting media folders update to all clients");
        await hubContext.Clients.All.MediaFoldersUpdated();
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
    }

    public async Task NotifyPromptFoldersUpdated()
    {
        logger.LogDebug("Broadcasting prompt folders update to all clients");
        await hubContext.Clients.All.PromptFoldersUpdated();
    }

    public async Task NotifyPromptFolderContentsUpdated(long folderId)
    {
        logger.LogDebug("Broadcasting prompt folder contents update for specific folder to all clients");
        await hubContext.Clients.All.PromptFolderContentsUpdated(folderId);
    }

    public async Task NotifyPromptUpdated(long id)
    {
        logger.LogDebug("Broadcasting single prompt update to all clients");
        await hubContext.Clients.All.PromptUpdated(id);
    }

    public async Task NotifyPromptPartFoldersUpdated()
    {
        logger.LogDebug("Broadcasting prompt part folders update to all clients");
        await hubContext.Clients.All.PromptPartFoldersUpdated();
    }

    public async Task NotifyPromptPartFolderContentsUpdated(long folderId)
    {
        logger.LogDebug("Broadcasting prompt part folder contents update for specific folder to all clients");
        await hubContext.Clients.All.PromptPartFolderContentsUpdated(folderId);
    }

    public async Task NotifyPromptPartUpdated(long id)
    {
        logger.LogDebug("Broadcasting single prompt part update to all clients");
        await hubContext.Clients.All.PromptPartUpdated(id);
    }

    public async Task OperationStatusUpdated(OperationStatusUpdate update)
    {
        await hubContext.Clients.All.OperationStatusUpdated(update);
    }

    public async Task NotifyRemoteModelFoldersUpdated()
    {
        logger.LogDebug("Broadcasting remote folders update to all clients");
        await hubContext.Clients.All.RemoteModelFoldersUpdated();
    }

    public async Task NotifyRemoteModelFolderContentsUpdated(long folderId)
    {
        logger.LogDebug("Broadcasting remote folder contents update to all clients");
        await hubContext.Clients.All.RemoteModelFolderContentsUpdated(folderId);
    }

    public async Task NotifyRemoteModelUpdated(long id)
    {
        logger.LogDebug("Broadcasting remote model update to all clients");
        await hubContext.Clients.All.RemoteModelUpdated(id);
    }

    public async Task NotifyDatasetsListUpdated()
    {
        logger.LogDebug("Broadcasting datasets list update to all clients");
        await hubContext.Clients.All.DatasetsListUpdated();
    }

    public async Task NotifyDatasetUpdated(long id)
    {
        logger.LogDebug("Broadcasting single dataset update to all clients");
        await hubContext.Clients.All.DatasetUpdated(id);
    }

    public async Task NotifyDatasetContentsUpdated(long datasetId)
    {
        logger.LogDebug("Broadcasting single dataset contents update to all clients");
        await hubContext.Clients.All.DatasetContentsUpdated(datasetId);
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
}
