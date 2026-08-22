using DualView.Shared.Models;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace DualView.Shared.Services;

/// <summary>
///   Helps in implementing the common signalR listeners.
/// </summary>
public abstract class SignalRServiceBase : ISignalRService
{
    public event Action? OnAppSettingsUpdated;

    public event Action? OnMediaFoldersUpdated;
    public event Action<long>? OnMediaFolderContentsUpdated;

    public event Action<long>? OnMediaUpdated;

    public event Action<long?>? OnUploadSectionActiveChanged;
    public event Action? OnUploadSectionsUpdated;
    public event Action<long>? OnUploadSectionUpdated;
    public event Action<long>? OnUploadSectionContentsUpdated;
    public event Action? OnMissingTagsUpdated;

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
        hubConnection.On(nameof(IDataHub.AppSettingsUpdated), () =>
        {
            Logger.LogInformation("Received general SettingsUpdated signal");
            OnAppSettingsUpdated?.Invoke();
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

        hubConnection.On(nameof(IDataHub.OperationStatusUpdated),
            (OperationStatusUpdate update) => { OnBackgroundOperationStatusUpdate?.Invoke(update); });

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

        hubConnection.On(nameof(IDataHub.MissingTagsUpdated), () => OnMissingTagsUpdated?.Invoke());
    }
}
