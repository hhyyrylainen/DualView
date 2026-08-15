using DualView.Shared.Models;
using DualView.Shared.Services;

namespace DualView.Server.ClientStubs;

/// <summary>
///   A stub for the SignalR service that doesn't actually send anything.
/// </summary>
public class StubSignalRService : ISignalRService
{
    // We don't care about these events as this is a stub
#pragma warning disable CS0067
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
    public event Action<OperationStatusUpdate>? OnBackgroundOperationStatusUpdate;
    public event Action? OnRemoteModelFoldersUpdated;
    public event Action<long>? OnRemoteModelFolderContentsUpdated;
    public event Action<long>? OnRemoteModelUpdated;
    public event Action? OnDatasetsListUpdated;
    public event Action<long>? OnDatasetUpdated;
    public event Action<long>? OnDatasetContentsUpdated;
    public event Action<long?>? OnUploadSectionActiveChanged;
    public event Action? OnFlowListUpdated;
    public event Action<long>? OnFlowUpdated;
    public event Action<bool>? OnConnectionStatusChanged;
#pragma warning restore CS0067

    /// <summary>
    ///   Pretend to be always connected for prerendering.
    /// </summary>
    public bool IsConnected => true;

    public Task StartConnectionAsync()
    {
        return Task.CompletedTask;
    }
}
