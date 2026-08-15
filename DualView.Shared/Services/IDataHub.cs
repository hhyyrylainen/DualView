using DualView.Shared.Models;

namespace DualView.Shared.Services;

public interface IDataHub
{
    public Task DualViewSettingsListUpdated();
    public Task DualViewSettingsUpdated(int id);

    public Task AppSettingsUpdated();

    public Task ChatsListUpdated();
    public Task ChatUpdated(long id);

    /// <summary>
    ///   New message added to a chat
    /// </summary>
    /// <returns>A task</returns>
    public Task ChatMessagesUpdated(long id);

    /// <summary>
    ///   Note that this doesn't specify the message generation, so all messages with the index are potentially updated
    /// </summary>
    /// <returns>A task</returns>
    public Task ChatMessageUpdated(long id, int index);

    public Task ChatMessagePrimaryUpdated(long id, int index, int generation);

    public Task WorldsListUpdated();
    public Task WorldUpdated(long id);
    public Task WorldChatsUpdated(long worldId);

    public Task WorkflowFoldersUpdated();

    public Task WorkflowListUpdated();
    public Task WorkflowUpdated(long id);

    public Task MediaFoldersUpdated();
    public Task MediaFolderContentsUpdated(long folderId);

    public Task MediaUpdated(long id);

    public Task PromptFoldersUpdated();
    public Task PromptFolderContentsUpdated(long folderId);
    public Task PromptUpdated(long id);

    public Task PromptPartFoldersUpdated();
    public Task PromptPartFolderContentsUpdated(long folderId);

    public Task PromptPartUpdated(long id);

    public Task OperationStatusUpdated(OperationStatusUpdate update);

    public Task RemoteModelFoldersUpdated();
    public Task RemoteModelFolderContentsUpdated(long folderId);
    public Task RemoteModelUpdated(long id);
    public Task DatasetsListUpdated();
    public Task DatasetUpdated(long id);
    public Task DatasetContentsUpdated(long datasetId);

    // DualView specific
    public Task CollectionUpdated(long id);
    public Task CollectionContentsUpdated(long id);
    public Task TagUpdated(long id);
    public Task TagsUpdated();
    public Task TagModifiersUpdated();
    public Task DownloadGalleriesUpdated();
    public Task DownloadGalleryUpdated(long id);
    public Task UploadSectionActiveChanged(long? sectionId);
}
