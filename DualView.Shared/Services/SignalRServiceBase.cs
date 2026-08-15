using DualView.Shared.Models;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace DualView.Shared.Services;

/// <summary>
///   Helps in implementing the common signalR listeners.
/// </summary>
public abstract class SignalRServiceBase : ISignalRService
{
    public event Action? OnRunnerSettingsUpdated;
    public event Action<int>? OnSingleRunnerSettingsUpdated;
    public event Action? OnAppSettingsUpdated;

    public event Action? OnChatsListUpdated;
    public event Action<long>? OnChatUpdated;
    public event Action<long>? OnChatMessagesUpdated;
    public event Action<long, int>? OnChatMessageUpdated;
    public event Action<long, int, int>? OnChatMessagePrimaryUpdated;
    public event Action? OnWorldsListUpdated;
    public event Action<long>? OnWorldUpdated;
    public event Action<long>? OnWorldChatsUpdated;

    public event Action? OnWorkflowFoldersUpdated;
    public event Action? OnWorkflowListUpdated;
    public event Action<long>? OnWorkflowUpdated;

    public event Action? OnMediaFoldersUpdated;
    public event Action<long>? OnMediaFolderContentsUpdated;

    public event Action<long>? OnMediaUpdated;

    public event Action? OnPromptFoldersUpdated;
    public event Action<long>? OnPromptFolderContentsUpdated;
    public event Action<long>? OnPromptUpdated;

    public event Action? OnPromptPartFoldersUpdated;
    public event Action<long>? OnPromptPartFolderContentsUpdated;
    public event Action<long>? OnPromptPartUpdated;

    public event Action? OnRemoteModelFoldersUpdated;
    public event Action<long>? OnRemoteModelFolderContentsUpdated;
    public event Action<long>? OnRemoteModelUpdated;
    public event Action? OnDatasetsListUpdated;
    public event Action<long>? OnDatasetUpdated;
    public event Action<long>? OnDatasetContentsUpdated;
    public event Action<long?>? OnUploadSectionActiveChanged;
    public event Action? OnUploadSectionsUpdated;
    public event Action<long>? OnUploadSectionUpdated;
    public event Action<long>? OnUploadSectionContentsUpdated;

    public event Action<OperationStatusUpdate>? OnBackgroundOperationStatusUpdate;

    public abstract event Action<bool>? OnConnectionStatusChanged;

    protected readonly ILogger Logger;

    protected SignalRServiceBase(ILogger logger)
    {
        Logger = logger;
    }

    public abstract bool IsConnected { get; }

    public abstract Task StartConnectionAsync();

    protected void RegisterBaseListeners(HubConnection hubConnection)
    {
        hubConnection.On(nameof(IDataHub.DualViewSettingsListUpdated), () =>
        {
            Logger.LogInformation("Received SettingsUpdated signal");
            OnRunnerSettingsUpdated?.Invoke();
        });

        hubConnection.On(nameof(IDataHub.DualViewSettingsUpdated), (int id) =>
        {
            Logger.LogInformation("Received SettingsUpdated signal");
            OnSingleRunnerSettingsUpdated?.Invoke(id);
        });

        hubConnection.On(nameof(IDataHub.AppSettingsUpdated), () =>
        {
            Logger.LogInformation("Received general SettingsUpdated signal");
            OnAppSettingsUpdated?.Invoke();
        });

        hubConnection.On(nameof(IDataHub.ChatsListUpdated), () =>
        {
            Logger.LogInformation("Received chats list update");
            OnChatsListUpdated?.Invoke();
        });

        hubConnection.On(nameof(IDataHub.ChatUpdated), (long id) =>
        {
            Logger.LogInformation("Received chat update");
            OnChatUpdated?.Invoke(id);
        });

        hubConnection.On(nameof(IDataHub.ChatMessagesUpdated), (long chatId) =>
        {
            Logger.LogInformation("Received chat messages list update");
            OnChatMessagesUpdated?.Invoke(chatId);
        });

        hubConnection.On(nameof(IDataHub.ChatMessageUpdated), (long chatId, int messageIndex) =>
        {
            Logger.LogInformation("Received chat message update");
            OnChatMessageUpdated?.Invoke(chatId, messageIndex);
        });

        hubConnection.On(nameof(IDataHub.ChatMessagePrimaryUpdated), (long chatId, int messageIndex, int generation) =>
        {
            Logger.LogInformation("Received chat message generation primary update");
            OnChatMessagePrimaryUpdated?.Invoke(chatId, messageIndex, generation);
        });

        hubConnection.On(nameof(IDataHub.WorldsListUpdated), () =>
        {
            Logger.LogInformation("Received world list update message");
            OnWorldsListUpdated?.Invoke();
        });

        hubConnection.On(nameof(IDataHub.WorldUpdated), (long id) =>
        {
            Logger.LogInformation("Received world update message");
            OnWorldUpdated?.Invoke(id);
        });

        hubConnection.On(nameof(IDataHub.WorldChatsUpdated), (long worldId) =>
        {
            Logger.LogInformation("Received world chat list update message");
            OnWorldChatsUpdated?.Invoke(worldId);
        });

        hubConnection.On(nameof(IDataHub.WorkflowFoldersUpdated), () =>
        {
            Logger.LogInformation("Received workflow folders update");
            OnWorkflowFoldersUpdated?.Invoke();
        });

        hubConnection.On(nameof(IDataHub.WorkflowListUpdated), () =>
        {
            Logger.LogInformation("Received workflow list update");
            OnWorkflowListUpdated?.Invoke();
        });

        hubConnection.On(nameof(IDataHub.WorkflowUpdated), (long workflowId) =>
        {
            Logger.LogInformation("Received workflow update");
            OnWorkflowUpdated?.Invoke(workflowId);
        });

        hubConnection.On(nameof(IDataHub.MediaFoldersUpdated), () =>
        {
            Logger.LogInformation("Received media folder list update");
            OnMediaFoldersUpdated?.Invoke();
        });

        hubConnection.On(nameof(IDataHub.MediaFolderContentsUpdated), (long folderId) =>
        {
            Logger.LogInformation("Received media folder contents update");
            OnMediaFolderContentsUpdated?.Invoke(folderId);
        });

        hubConnection.On(nameof(IDataHub.MediaUpdated), (long id) =>
        {
            Logger.LogInformation("Received media update");
            OnMediaUpdated?.Invoke(id);
        });

        hubConnection.On(nameof(IDataHub.PromptFoldersUpdated), () =>
        {
            Logger.LogInformation("Received prompt folder list update");
            OnPromptFoldersUpdated?.Invoke();
        });

        hubConnection.On(nameof(IDataHub.PromptFolderContentsUpdated), (long folderId) =>
        {
            Logger.LogInformation("Received prompt folder contents update");
            OnPromptFolderContentsUpdated?.Invoke(folderId);
        });

        hubConnection.On(nameof(IDataHub.PromptUpdated), (long id) =>
        {
            Logger.LogInformation("Received prompt update");
            OnPromptUpdated?.Invoke(id);
        });

        hubConnection.On(nameof(IDataHub.PromptPartFoldersUpdated), () =>
        {
            Logger.LogInformation("Received prompt part folder list update");
            OnPromptPartFoldersUpdated?.Invoke();
        });

        hubConnection.On(nameof(IDataHub.PromptPartFolderContentsUpdated), (long folderId) =>
        {
            Logger.LogInformation("Received prompt part folder contents update");
            OnPromptPartFolderContentsUpdated?.Invoke(folderId);
        });

        hubConnection.On(nameof(IDataHub.PromptPartUpdated), (long id) =>
        {
            Logger.LogInformation("Received prompt part update");
            OnPromptPartUpdated?.Invoke(id);
        });

        hubConnection.On(nameof(IDataHub.OperationStatusUpdated),
            (OperationStatusUpdate update) => { OnBackgroundOperationStatusUpdate?.Invoke(update); });

        hubConnection.On(nameof(IDataHub.RemoteModelFoldersUpdated), () =>
        {
            Logger.LogInformation("Received remote model folder list update");
            OnRemoteModelFoldersUpdated?.Invoke();
        });

        hubConnection.On(nameof(IDataHub.RemoteModelFolderContentsUpdated), (long folderId) =>
        {
            Logger.LogInformation("Received remote model folder contents update");
            OnRemoteModelFolderContentsUpdated?.Invoke(folderId);
        });

        hubConnection.On(nameof(IDataHub.RemoteModelUpdated), (long id) =>
        {
            Logger.LogInformation("Received remote model update");
            OnRemoteModelUpdated?.Invoke(id);
        });

        hubConnection.On(nameof(IDataHub.DatasetsListUpdated), () =>
        {
            Logger.LogInformation("Received datasets list update");
            OnDatasetsListUpdated?.Invoke();
        });

        hubConnection.On(nameof(IDataHub.DatasetUpdated), (long id) =>
        {
            Logger.LogInformation("Received dataset update");
            OnDatasetUpdated?.Invoke(id);
        });

        hubConnection.On(nameof(IDataHub.DatasetContentsUpdated), (long datasetId) =>
        {
            Logger.LogInformation("Received dataset contents update");
            OnDatasetContentsUpdated?.Invoke(datasetId);
        });

        hubConnection.On(nameof(IDataHub.UploadSectionActiveChanged), (long? sectionId) =>
        {
            Logger.LogInformation("Received upload section active update");
            OnUploadSectionActiveChanged?.Invoke(sectionId);
        });

        hubConnection.On(nameof(IDataHub.UploadSectionsUpdated), () =>
        {
            Logger.LogInformation("Received upload sections list update");
            OnUploadSectionsUpdated?.Invoke();
        });

        hubConnection.On(nameof(IDataHub.UploadSectionUpdated), (long sectionId) =>
        {
            Logger.LogInformation("Received upload section update");
            OnUploadSectionUpdated?.Invoke(sectionId);
        });

        hubConnection.On(nameof(IDataHub.UploadSectionContentsUpdated), (long sectionId) =>
        {
            Logger.LogInformation("Received upload section contents update");
            OnUploadSectionContentsUpdated?.Invoke(sectionId);
        });
    }
}
