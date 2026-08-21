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
    private const int MediaPageSize = 100;

    private readonly ILogger<RestoreDeletedWindowViewModel>? logger;
    private readonly IClientDatabaseService? databaseService;
    private readonly IBackendAPI? backendAPI;
    private readonly IWindowService? windowService;
    private readonly IServiceProvider? serviceProvider;

    private int mediaPageIndex;

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
    public ObservableCollection<DeletedFolderViewModel> DeletedFolders { get; } = new();
    public ObservableCollection<DeletedCollectionViewModel> DeletedCollections { get; } = new();

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

    public bool HasPreviousMediaPage => mediaPageIndex > 0;

    public bool HasNextMediaPage
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public int MediaPageNumber => mediaPageIndex + 1;

    public void PreviousMediaPage()
    {
        if (!HasPreviousMediaPage)
            return;

        --mediaPageIndex;
        OnPropertyChanged(nameof(HasPreviousMediaPage));
        OnPropertyChanged(nameof(MediaPageNumber));
        _ = LoadMedia();
    }

    public void NextMediaPage()
    {
        if (!HasNextMediaPage)
            return;

        ++mediaPageIndex;
        OnPropertyChanged(nameof(HasPreviousMediaPage));
        OnPropertyChanged(nameof(MediaPageNumber));
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
            case 2:
                _ = LoadFolders();
                break;
            case 3:
                _ = LoadCollections();
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
            var media = await databaseService.GetDeletedMediaAsync(MediaPageSize + 1,
                mediaPageIndex * MediaPageSize);
            HasNextMediaPage = media.Count > MediaPageSize;

            var groups = media.Take(MediaPageSize).GroupBy(m => m.Id)
                .Select(g =>
                {
                    var first = g.First();
                    var group = new DeletedMediaGroupViewModel(first);
                    foreach (var m in g)
                    {
                        group.Configurations.Add(
                            new DeletedMediaConfigViewModel(new ConfiguredMediaDTO(m), StartMediaRestore,
                                serviceProvider!));
                    }

                    return group;
                }).ToList();

            Dispatcher.UIThread.Post(() =>
            {
                foreach (var group in DeletedMedia)
                {
                    foreach (var config in group.Configurations)
                    {
                        config.Dispose();
                    }
                }

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
            await WarnAboutUnbalancedPairedCollections(configId);

            logger?.LogInformation("Restored media config {Id}", configId);
            await LoadMedia();
        }
        catch (Exception e)
        {
            logger?.LogError(e, "Failed to restore media config {Id}", configId);
            windowService?.ShowErrorWindow($"Failed to restore media config {configId}", e);
        }
    }

    private async Task WarnAboutUnbalancedPairedCollections(long mediaId)
    {
        if (databaseService == null || windowService == null)
            return;

        var unbalancedCollections = new System.Collections.Generic.List<string>();
        foreach (var collectionId in await databaseService.GetMediaCollectionsAsync(mediaId))
        {
            var collection = await databaseService.GetCollectionAsync(collectionId);
            if (collection == null || collection.ImageGroupSize <= 1)
                continue;

            var contents = await databaseService.GetCollectionContents(collectionId);
            var activeCount = contents.Count(media => !media.IsDeleted);
            if (activeCount % collection.ImageGroupSize != 0)
                unbalancedCollections.Add(collection.Name);
        }

        if (unbalancedCollections.Count > 0)
        {
            windowService.ShowNoticeWindow(
                $"Restoring this image made these paired collections unbalanced: {string.Join(", ", unbalancedCollections)}.",
                "Warning");
        }
    }

    private async Task LoadFolders()
    {
        if (databaseService == null) return;
        try
        {
            var folders = await databaseService.GetDeletedMediaFoldersAsync(100);
            Dispatcher.UIThread.Post(() =>
            {
                DeletedFolders.Clear();
                foreach (var f in folders)
                    DeletedFolders.Add(new DeletedFolderViewModel(f, StartFolderRestore));
            });
        }
        catch (Exception e)
        {
            logger?.LogError(e, "Failed to load deleted folders");
        }
    }

    private void StartFolderRestore(long id)
    {
        _ = PerformFolderRestore(id);
    }

    private async Task PerformFolderRestore(long id)
    {
        if (databaseService == null) return;
        try
        {
            await databaseService.RestoreMediaFolderAsync(id);
            Dispatcher.UIThread.Post(() =>
            {
                var folder = DeletedFolders.FirstOrDefault(f => f.Id == id);
                if (folder != null) DeletedFolders.Remove(folder);
            });
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Failed to restore folder", e);
        }
    }

    private async Task LoadCollections()
    {
        if (databaseService == null) return;
        try
        {
            var collections = await databaseService.GetDeletedCollectionsAsync(100);
            Dispatcher.UIThread.Post(() =>
            {
                DeletedCollections.Clear();
                foreach (var c in collections)
                    DeletedCollections.Add(new DeletedCollectionViewModel(c, StartCollectionRestore));
            });
        }
        catch (Exception e)
        {
            logger?.LogError(e, "Failed to load deleted collections");
        }
    }

    private void StartCollectionRestore(long id)
    {
        _ = PerformCollectionRestore(id);
    }

    private async Task PerformCollectionRestore(long id)
    {
        if (databaseService == null) return;
        try
        {
            await databaseService.RestoreCollectionAsync(id);
            Dispatcher.UIThread.Post(() =>
            {
                var collection = DeletedCollections.FirstOrDefault(c => c.Id == id);
                if (collection != null) DeletedCollections.Remove(collection);
            });
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Failed to restore collection", e);
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
                MediaOpenResources = new ShowMediaInSeparateWindow(
                    serviceProvider.GetRequiredService<IWindowService>(), null),
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

    public class DeletedFolderViewModel(MediaFolderDTO folder, Action<long> onRequestRestore) : ViewModelBase
    {
        public long Id => folder.Id;
        public string Name => folder.Name;
        public string DeletedAt => folder.UpdatedAt.ToLocalTime().ToString("g");

        public void Restore() => onRequestRestore(Id);
    }

    public class DeletedCollectionViewModel(CollectionDTO collection, Action<long> onRequestRestore) : ViewModelBase
    {
        public long Id => collection.Id;
        public string Name => collection.Name;
        public string DeletedAt => collection.UpdatedAt.ToLocalTime().ToString("g");

        public void Restore() => onRequestRestore(Id);
    }
}
