using System;
using DualView.GUI.Services;
using Avalonia;
using Avalonia.Threading;
using Backend.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DualView.GUI.ViewModels;

public class ConnectionFailedViewModel : ViewModelBase, IDisposable
{
    private readonly ILogger<ConnectionFailedViewModel>? logger;
    private readonly IBackendStatusService? backendStatusService;
    private readonly IWindowService? windowService;
    private readonly IDataFolderService? dataFolderService;

    // Default constructor for design-time
    public ConnectionFailedViewModel()
    {
    }

    [ActivatorUtilitiesConstructor]
    public ConnectionFailedViewModel(ILogger<ConnectionFailedViewModel> logger,
        IBackendStatusService backendStatusService, IWindowService windowService, IDataFolderService dataFolderService)
    {
        this.logger = logger;
        this.backendStatusService = backendStatusService;
        this.windowService = windowService;
        this.dataFolderService = dataFolderService;

        logger.LogInformation("ConnectionFailedViewModel initialized");

        backendStatusService.OnStatusChanged += OnCheckBackend;
    }

    public string ExtraMessage
    {
        get => field;
        set => SetProperty(ref field, value);
    } = "";

    public bool WantsToClose
    {
        get => field;
        set => SetProperty(ref field, value);
    }

    public string DataFolder => dataFolderService?.GetDataFolderPath() ?? "~/.local/share/DualView";

    public void Dispose()
    {
        if (backendStatusService != null)
            backendStatusService.OnStatusChanged -= OnCheckBackend;

        logger?.LogInformation("ConnectionFailedViewModel disposed");
    }

    private void OnCheckBackend(bool newValue)
    {
        if (!newValue || backendStatusService?.IsConnected != true)
        {
            logger?.LogInformation("Not connected to backend yet");
            return;
        }

        logger?.LogInformation("Backend has become available! Opening main window...");

        // We need to perform on the UI Thread
        Dispatcher.UIThread.Post(() =>
        {
            var app = Application.Current;

            if (app is App appInstance)
            {
                appInstance.ShowOrActivateMainWindow();
            }
            else
            {
                logger?.LogWarning("No Avalonia app instance found, doing a backup main window show");
                windowService?.ShowSingletonWindow<MainWindowViewModel>();
            }

            WantsToClose = true;
        });
    }
}
