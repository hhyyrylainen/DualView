using DualView.Shared.Models;

namespace Backend.Services;

public interface IEntityUpdateNotifier
{
    public Task NotifyDualViewSettingsListUpdated();

    public Task NotifyDualViewSettingsUpdated(int id);

    public Task NotifyAppSettingsUpdated();

    public Task NotifyChatsListUpdated();

    public Task NotifyChatUpdated(long id);
    public Task ChatMessagesUpdated(long id);
    public Task NotifyChatMessageUpdated(long id, int index);
    public Task NotifyChatMessagePrimaryUpdated(long id, int index, int generation);

    public Task NotifyWorldsListUpdated();
    public Task NotifyWorldUpdated(long id);

    public Task NotifyWorldChatsListUpdated(long worldId);

    public Task NotifyWorkflowFoldersUpdated();
    public Task NotifyWorkflowListUpdated();
    public Task NotifyWorkflowUpdated(long id);

    public Task NotifyMediaFoldersUpdated();
    public Task NotifyMediaFolderContentsUpdated(long folderId);

    public Task NotifyMediaUpdated(long id);

    public Task NotifyPromptFoldersUpdated();
    public Task NotifyPromptFolderContentsUpdated(long folderId);
    public Task NotifyPromptUpdated(long id);

    public Task NotifyPromptPartFoldersUpdated();
    public Task NotifyPromptPartFolderContentsUpdated(long folderId);
    public Task NotifyPromptPartUpdated(long id);

    public Task OperationStatusUpdated(OperationStatusUpdate update);

    public Task NotifyRemoteModelFoldersUpdated();
    public Task NotifyRemoteModelFolderContentsUpdated(long folderId);
    public Task NotifyRemoteModelUpdated(long id);
    public Task NotifyDatasetsListUpdated();
    public Task NotifyDatasetUpdated(long id);
    public Task NotifyDatasetContentsUpdated(long datasetId);
}
