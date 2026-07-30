using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using DualView.GUI.Services;
using DualView.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DualView.GUI.ViewModels;

public class MaintenanceToolsWindowViewModel : ViewModelBase
{
    private readonly ILogger<MaintenanceToolsWindowViewModel>? logger;
    private readonly IClientDatabaseService? databaseService;
    private readonly IWindowService? windowService;
    private readonly IBackendAPI? backendAPI;

    public MaintenanceToolsWindowViewModel()
    {
        // Design time
    }

    [ActivatorUtilitiesConstructor]
    public MaintenanceToolsWindowViewModel(ILogger<MaintenanceToolsWindowViewModel> logger,
        IClientDatabaseService databaseService, IWindowService windowService, IBackendAPI backendAPI)
    {
        this.logger = logger;
        this.databaseService = databaseService;
        this.windowService = windowService;
        this.backendAPI = backendAPI;
    }

    public ObservableCollection<string> Results { get; } = new();

    public string StatusText
    {
        get;
        set => SetProperty(ref field, value);
    } = "Idle";

    public double Progress
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool IsRunning
    {
        get;
        set => SetProperty(ref field, value);
    }

    public async Task StartImageExistCheck()
    {
        if (IsRunning || backendAPI == null) return;
        IsRunning = true;
        StatusText = "Checking if all images exist...";
        Results.Add("Starting image exist check...");

        try
        {
            var opId = await backendAPI.StartImageExistCheck();
            windowService?.ShowOperationStatus(opId);
            Results.Add($"Started image check operation: {opId}");
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Failed to start check", e);
        }

        IsRunning = false;
    }

    public async Task DeleteThumbnails()
    {
        if (IsRunning || backendAPI == null) return;
        IsRunning = true;
        StatusText = "Deleting thumbnails...";
        Results.Add("Starting thumbnail deletion...");

        try
        {
            var opId = await backendAPI.StartDeleteThumbnails();
            windowService?.ShowOperationStatus(opId);
            Results.Add($"Started thumbnail deletion operation: {opId}");
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Failed to delete thumbnails", e);
        }

        IsRunning = false;
    }

    public async Task FixOrphanedResources()
    {
        if (IsRunning || backendAPI == null) return;
        IsRunning = true;
        StatusText = "Fixing orphaned resources...";
        Results.Add("Starting orphaned resource fix...");

        try
        {
            var opId = await backendAPI.StartFixOrphanedResources();
            windowService?.ShowOperationStatus(opId);
            Results.Add($"Started orphaned resource fix operation: {opId}");
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Failed to start fix", e);
        }

        IsRunning = false;
    }

    public void ClearResults()
    {
        Results.Clear();
    }

    public void Cancel()
    {
        // TODO: cancellation token
        IsRunning = false;
        StatusText = "Canceled";
    }
}
