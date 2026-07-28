using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using DualView.GUI.Models;
using DualView.GUI.Services;
using DualView.Shared.Models.DTO;
using DualView.Shared.Services;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DualView.GUI.ViewModels;

public class RestoreDeletedWindowViewModel : ViewModelBase, IDisposable
{
    private readonly ILogger<RestoreDeletedWindowViewModel>? logger;
    private readonly IClientDatabaseService? databaseService;
    private readonly IBackendAPI? backendAPI;
    private readonly IWindowService? windowService;
    private readonly IServiceProvider? serviceProvider;

    // Design time constructor
    public RestoreDeletedWindowViewModel()
    {
        Hamburger = new HamburgerMenuViewModel();
    }

    [ActivatorUtilitiesConstructor]
    public RestoreDeletedWindowViewModel(ILogger<RestoreDeletedWindowViewModel> logger,
        IClientDatabaseService databaseService, IBackendAPI backendAPI, IWindowService windowService,
        IBackendStatusService backendStatusService, IServiceProvider serviceProvider)
    {
        this.logger = logger;
        this.databaseService = databaseService;
        this.backendAPI = backendAPI;
        this.windowService = windowService;
        this.serviceProvider = serviceProvider;

        Hamburger = new HamburgerMenuViewModel(backendStatusService);

        InitializeMenu();

        Refresh();
    }

    public ObservableCollection<DeletedMediaGroupViewModel> DeletedMedia { get; } = new();

    public HamburgerMenuViewModel Hamburger { get; }

    public int TabIndex
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                Refresh();
            }
        }
    } = 0;

    public void RefreshJobs()
    {
        _ = LoadJobs();
    }

    public void RefreshMedia()
    {
        _ = LoadMedia();
    }

    public void Refresh()
    {
        switch (TabIndex)
        {
            case 0:
                RefreshJobs();
                break;
            case 1:
                RefreshMedia();
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public void Dispose()
    {
        Hamburger.Dispose();

        foreach (var group in DeletedMedia)
        {
            foreach (var config in group.Configurations)
            {
                config.Dispose();
            }
        }

        DeletedMedia.Clear();
    }

    private void StartJobRestore(long jobId)
    {
        logger?.LogInformation("Restoring job {JobId}", jobId);

        _ = PerformJobRestore(jobId);
    }

    private void StartMediaRestore(long configId)
    {
        logger?.LogInformation("Restoring media config {Id}", configId);

        _ = PerformMediaRestore(configId);
    }

    private async Task LoadJobs()
    {
        if (databaseService == null)
            return;

        try
        {
        }
        catch (Exception e)
        {
            logger?.LogError(e, "Failed to load jobs");
            windowService?.ShowErrorWindow("Failed to load jobs", e);
        }
    }

    private async Task PerformJobRestore(long jobId)
    {
        if (databaseService == null)
            return;

        try
        {
        }
        catch (Exception e)
        {
            logger?.LogError(e, "Failed to restore job {JobId}", jobId);
            windowService?.ShowErrorWindow($"Failed to restore job {jobId}", e);
        }
    }

    private async Task LoadMedia()
    {
        if (databaseService == null)
            return;

        try
        {
            var media = await databaseService.GetDeletedMediaAsync(100);

            var groups = media.GroupBy(m => m.Id)
                .Select(g =>
                {
                    var first = g.First();
                    var group = new DeletedMediaGroupViewModel(first);
                    foreach (var m in g)
                    {
                        group.Configurations.Add(
                            new DeletedMediaConfigViewModel(new ConfiguredMediaDTO(m), StartMediaRestore, serviceProvider!));
                    }

                    return group;
                }).ToList();

            Dispatcher.UIThread.Post(() =>
            {
                DeletedMedia.Clear();
                foreach (var group in groups)
                {
                    DeletedMedia.Add(group);
                }
            });
        }
        catch (Exception e)
        {
            logger?.LogError(e, "Failed to load deleted media");
            windowService?.ShowErrorWindow("Failed to load deleted media", e);
        }
    }

    private async Task PerformMediaRestore(long configId)
    {
        if (databaseService == null)
            return;

        try
        {
            await databaseService.RestoreMediaAsync(configId);

            Dispatcher.UIThread.Post(() =>
            {
                logger?.LogInformation("Restored media config {Id}", configId);

                // Find and remove the config from the list
                foreach (var group in DeletedMedia)
                {
                    var config = group.Configurations.FirstOrDefault(c => c.Id == configId);
                    if (config != null)
                    {
                        config.Dispose();
                        group.Configurations.Remove(config);
                        if (group.Configurations.Count == 0)
                        {
                            DeletedMedia.Remove(group);
                        }

                        break;
                    }
                }
            });
        }
        catch (Exception e)
        {
            logger?.LogError(e, "Failed to restore media config {Id}", configId);
            windowService?.ShowErrorWindow($"Failed to restore media config {configId}", e);
        }
    }

    private void InitializeMenu()
    {
        MainWindowViewModel.AddDefaultMenuItems(Hamburger);

        Hamburger.MenuItems.Add(new HamburgerMenuItem
            { Title = "Refresh", Command = new RelayCommand(Refresh) });

        MainWindowViewModel.AddTrailingMenuItems(Hamburger, windowService);
    }

    public class DeletedMediaGroupViewModel : ViewModelBase
    {
        public DeletedMediaGroupViewModel(MediaFileDTO mediaFile)
        {
            MediaFileId = mediaFile.Id;
            OriginalFileName = mediaFile.OriginalFileName;
        }

        public long MediaFileId { get; }
        public string OriginalFileName { get; }
        public ObservableCollection<DeletedMediaConfigViewModel> Configurations { get; } = new();
    }

    public class DeletedMediaConfigViewModel : ViewModelBase, IDisposable
    {
        private readonly ConfiguredMediaDTO config;
        private readonly Action<long> onRequestRestore;

        public DeletedMediaConfigViewModel(ConfiguredMediaDTO config, Action<long> onRequestRestore,
            IServiceProvider serviceProvider)
        {
            this.config = config;
            this.onRequestRestore = onRequestRestore;

            Viewer = new MediaViewerViewModel(serviceProvider.GetRequiredService<ILogger<MediaViewerViewModel>>(),
                serviceProvider.GetRequiredService<IWindowService>())
            {
                MediaToShow = new ServerMediaSource(new ConfiguredMediaInfo(config), serviceProvider),
                ShowName = false,
                AutoThumbnailSize = false,
                ShowingThumbnail = true,
            };
        }

        public long Id => config.Id;
        public string Name => config.Name;
        public string DeletedAt => config.UpdatedAt.ToLocalTime().ToString("g");

        public MediaViewerViewModel Viewer { get; }

        public void Restore()
        {
            onRequestRestore(Id);
        }

        public void Dispose()
        {
            Viewer.Dispose();
        }
    }
}
