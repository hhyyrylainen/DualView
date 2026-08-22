using DualView.Shared.Models;

namespace DualView.Shared.Services;

public interface ISignalRService
{
    // Events that components can subscribe to
    public event Action? OnAppSettingsUpdated;

    public event Action? OnMediaFoldersUpdated;
    public event Action<long>? OnMediaFolderContentsUpdated;

    public event Action<long>? OnMediaUpdated;

    public event Action<OperationStatusUpdate>? OnBackgroundOperationStatusUpdate;

    public event Action<long?>? OnUploadSectionActiveChanged;
    public event Action? OnUploadSectionsUpdated;
    public event Action<long>? OnUploadSectionUpdated;
    public event Action<long>? OnUploadSectionContentsUpdated;
    public event Action? OnMissingTagsUpdated;

    // True = Connected, False = Disconnected
    public event Action<bool>? OnConnectionStatusChanged;

    public bool IsConnected { get; }

    public Task StartConnectionAsync();
}
