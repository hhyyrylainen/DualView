using DualView.Shared.Models;

namespace DualView.Shared.Services;

public interface ISignalRService
{
    // Events that components can subscribe to
    public event Action? OnRunnerSettingsUpdated;
    public event Action<int>? OnSingleRunnerSettingsUpdated;

    public event Action? OnAppSettingsUpdated;

    public event Action? OnChatsListUpdated;
    public event Action<long>? OnChatUpdated;
    public event Action<long>? OnChatMessagesUpdated;

    // The second parameter is the index
    public event Action<long, int>? OnChatMessageUpdated;

    // The second parameter is the index, the third is the generation
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

    public event Action<OperationStatusUpdate>? OnBackgroundOperationStatusUpdate;

    public event Action? OnRemoteModelFoldersUpdated;
    public event Action<long>? OnRemoteModelFolderContentsUpdated;
    public event Action<long>? OnRemoteModelUpdated;
    public event Action? OnDatasetsListUpdated;
    public event Action<long>? OnDatasetUpdated;
    public event Action<long>? OnDatasetContentsUpdated;

    // True = Connected, False = Disconnected
    public event Action<bool>? OnConnectionStatusChanged;

    public bool IsConnected { get; }

    public Task StartConnectionAsync();
}
