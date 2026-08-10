using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using DualView.GUI.Models;
using DualView.GUI.Services;
using DualView.Shared.Models.DTO;
using DualView.Shared.Models.Enums;
using DualView.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DualView.GUI.ViewModels;

/// <summary>
///   The main collection view of media folders and items.
/// </summary>
public class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly ILogger<MainWindowViewModel>? logger;
    private readonly IClientDatabaseService? databaseService;
    private readonly IWindowService? windowService;
    private readonly IBackendStatusService? backendStatusService;
    private readonly IBackgroundJobs? backgroundJobs;
    private readonly ISignalRService? signalRService;
    private readonly IBackendAPI? backendAPI;
    private readonly IServiceProvider? serviceProvider;
    private readonly MainWindowMediaActions? mediaActions;

    private readonly Stack<(string Path, Vector ScrollOffset)> navigationHistory = new();

    private string currentPath = "/";
    private long? currentFolderId;
    private long? currentCollectionId;
    private string searchText = string.Empty;

    private Vector? pendingScrollOffsetRestore;

    private int currentPage = 1;
    private int totalPages = 1;
    private int pageSize = 100;

    // Default constructor for design-time
    public MainWindowViewModel()
    {
        Hamburger = new HamburgerMenuViewModel();
        InitializeMenu();

        // Add a test item to view in the designer
        MainItems.Add(new MediaViewerViewModel
        {
            AllowSelection = false,
            ShowingThumbnail = true,
            Name = "Test item!",
        });
    }

    [ActivatorUtilitiesConstructor]
    public MainWindowViewModel(ILogger<MainWindowViewModel> logger, IClientDatabaseService databaseService,
        IWindowService windowService, IBackendStatusService backendStatusService, IBackgroundJobs backgroundJobs,
        ISignalRService signalRService, IBackendAPI backendAPI, IServiceProvider serviceProvider)
    {
        this.logger = logger;
        this.databaseService = databaseService;
        this.windowService = windowService;
        this.backendStatusService = backendStatusService;
        this.backgroundJobs = backgroundJobs;
        this.signalRService = signalRService;
        this.backendAPI = backendAPI;
        this.serviceProvider = serviceProvider;

        mediaActions = new MainWindowMediaActions(this, windowService);
        Hamburger = new HamburgerMenuViewModel(backendStatusService);

        InitializeMenu();

        logger.LogInformation("MainWindowViewModel initialized");

        backendStatusService.OnStatusChanged += OnBackendConnectionChanged;
        // backgroundJobs.Schedule(RefreshBackendStatusString, TimeSpan.FromSeconds(1));

        signalRService.OnMediaFoldersUpdated += OnMediaFoldersUpdated;
        signalRService.OnMediaFolderContentsUpdated += OnMediaFolderContentsUpdated;

        _ = RefreshItems();
        RefreshBackendStatusString();
    }

    public ObservableCollection<MediaViewerViewModel> MainItems { get; } = new();

    public string StatusText
    {
        get;
        set => SetProperty(ref field, value);
    } = "Contacting backend...";

    public string CurrentPath
    {
        get => currentPath;
        set => SetProperty(ref currentPath, value);
    }

    public Vector MainScrollOffset
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value))
            {
                currentPage = 1;
            }
        }
    }

    public bool IsRecursiveSearch
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                currentPage = 1;
                _ = RefreshItems();
            }
        }
    }

    public int CurrentPage
    {
        get => currentPage;
        set
        {
            if (SetProperty(ref currentPage, value))
            {
                OnPropertyChanged(nameof(CanNavigateBackwards));
                OnPropertyChanged(nameof(CanNavigateForwards));
                _ = RefreshItems();
            }
        }
    }

    public int TotalPages
    {
        get => totalPages;
        set
        {
            if (SetProperty(ref totalPages, value))
            {
                OnPropertyChanged(nameof(CanNavigateBackwards));
                OnPropertyChanged(nameof(CanNavigateForwards));
            }
        }
    }

    public bool CanNavigateBackwards => currentPage > 1;
    public bool CanNavigateForwards => currentPage < totalPages;

    public string? CurrentCollectionName
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsInCollection));
            }
        }
    }

    public bool IsInCollection => currentCollectionId != null;

    public HamburgerMenuViewModel Hamburger { get; }

    public static void AddDefaultMenuItems(HamburgerMenuViewModel hamburger)
    {
        hamburger.MenuItems.Clear();

        hamburger.MenuItems.Add(new HamburgerMenuItem
        {
            Title = "Home", Command = new RelayCommand(() =>
            {
                var app = Application.Current;
                ((App?)app)?.ShowOrActivateMainWindow();
            })
        });
    }

    public static void AddTrailingMenuItems(HamburgerMenuViewModel hamburger, IWindowService? windowService)
    {
        if (windowService != null)
        {
            hamburger.MenuItems.Add(new HamburgerMenuItem
            {
                Title = "About",
                Command = new RelayCommand(() => windowService.ShowSingletonWindow<AboutWindowViewModel>())
            });
        }

        hamburger.MenuItems.Add(new HamburgerMenuItem
        {
            Title = "Quit", Command = new RelayCommand(() =>
            {
                var app = Application.Current;
                // Graceful shutdown (Desktop lifetime)
                (app?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
            })
        });
    }

    public async Task Refresh()
    {
        await RefreshItems();
    }

    public async Task NavigateToEnteredPath()
    {
        navigationHistory.Clear();
        pendingScrollOffsetRestore = null;
        currentCollectionId = null;
        CurrentCollectionName = null;
        MainScrollOffset = new Vector(0, 0);
        await RefreshItems();
    }

    public void NavigateUp()
    {
        if (navigationHistory.Count > 0)
        {
            currentCollectionId = null;
            CurrentCollectionName = null;

            var previousLocation = navigationHistory.Pop();
            CurrentPath = previousLocation.Path;
            pendingScrollOffsetRestore = previousLocation.ScrollOffset;

            currentPage = 1;
            OnPropertyChanged(nameof(CurrentPage));
            _ = RefreshItems();
            return;
        }

        pendingScrollOffsetRestore = null;

        if (string.IsNullOrEmpty(currentPath) || currentPath == "/")
            return;

        var lastSlash = currentPath.LastIndexOf('/');
        if (lastSlash <= 0)
        {
            CurrentPath = "/";
        }
        else
        {
            CurrentPath = currentPath.Substring(0, lastSlash);
        }

        currentPage = 1;
        OnPropertyChanged(nameof(CurrentPage));
        _ = RefreshItems();
    }

    public async void NavigateToFolder(long id)
    {
        if (databaseService == null)
            return;

        try
        {
            var path = await databaseService.GetMediaFolderPath(id);
            navigationHistory.Push((CurrentPath, MainScrollOffset));
            pendingScrollOffsetRestore = null;
            currentFolderId = id;
            currentCollectionId = null;
            CurrentCollectionName = null;
            CurrentPath = path;
            MainScrollOffset = new Vector(0, 0);

            // Directly set value to not cause another refresh
            currentPage = 1;
            OnPropertyChanged(nameof(CurrentPage));

            // As we're done just one guaranteed refresh here
            _ = RefreshItems();
        }
        catch (Exception e)
        {
            logger?.LogError(e, "Failed to navigate to folder {Id}", id);
            windowService?.ShowErrorWindow("Failed to navigate to folder", e);
        }
    }

    public void OpenCollection(long id, string name)
    {
        navigationHistory.Push((CurrentPath, MainScrollOffset));
        pendingScrollOffsetRestore = null;
        currentCollectionId = id;
        CurrentCollectionName = name;
        MainScrollOffset = new Vector(0, 0);
        currentPage = 1;
        OnPropertyChanged(nameof(CurrentPage));
        _ = RefreshItems();
    }

    public void GoToNextPage()
    {
        if (currentPage < totalPages)
        {
            CurrentPage = currentPage + 1;
        }
    }

    public void GoToPreviousPage()
    {
        if (currentPage > 1)
        {
            CurrentPage = currentPage - 1;
        }
    }

    public void OpenFullView()
    {
        if (currentCollectionId == null)
            return;

        windowService?.ShowSingletonWindow<MediaCollectionWindowViewModel>();
    }

    public async Task RefreshItems()
    {
        if (databaseService == null || logger == null || windowService == null)
            return;

        try
        {
            if (currentCollectionId == null)
            {
                var folder = await databaseService.GetMediaFolderFromPathAsync(currentPath);
                if (folder != null)
                {
                    currentFolderId = folder.Id;
                }
                else
                {
                    // If path is invalid, fall back to root
                    currentFolderId = MediaFolderInfo.RootFolderId;
                    CurrentPath = "/";
                }
            }

            List<MediaViewerViewModel> newItems = new();
            int totalItemsCount;

            if (currentCollectionId != null)
            {
                var (contents, totalItems) = await databaseService.GetCollectionContents(currentCollectionId.Value,
                    currentPage - 1, pageSize, FolderSortColumn.Name, SortDirection.Ascending, searchText);

                totalItemsCount = totalItems;

                foreach (var item in contents)
                {
                    newItems.Add(new MediaViewerViewModel(logger, windowService)
                    {
                        Name = item.OriginalFileName,
                        MediaToShow = new ServerMediaSource(new ConfiguredMediaInfo(item), serviceProvider!),
                        MediaOpenResources = new ShowMediaInSeparateWindow(windowService),
                        ShowingThumbnail = true,
                        AllowSelection = false,
                    });
                }
            }
            else
            {
                var (content, totalItems) = await databaseService.GetMediaFolderContents(currentFolderId!.Value,
                    currentPage - 1, pageSize, FolderSortColumn.Name, SortDirection.Ascending, searchText);

                totalItemsCount = totalItems;

                // Server sorts items already
                foreach (var item in content)
                {
                    newItems.Add(new MediaViewerViewModel(logger, windowService)
                    {
                        Name = item.Name,
                        MediaToShow = new ServerMediaSource(item, serviceProvider!),
                        MediaOpenResources = mediaActions,
                        ShowingThumbnail = true,
                        AllowSelection = false,
                    });
                }
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                TotalPages = Math.Max(1, (int)Math.Ceiling((double)totalItemsCount / pageSize));

                // TODO: make a smarter refresh
                foreach (var item in MainItems)
                {
                    item.Dispose();
                }

                MainItems.Clear();
                foreach (var item in newItems)
                {
                    MainItems.Add(item);
                }

                if (pendingScrollOffsetRestore is { } scrollOffset)
                {
                    pendingScrollOffsetRestore = null;

                    _ = Task.Run(async () =>
                    {
                        // The item controls need to be measured before the offset can be
                        // restored; otherwise ScrollViewer clamps it to the new empty extent.
                        // There was a different bug, so this delay might not be needed,
                        // but it is low enough to be human-imperceptible.
                        await Task.Delay(50);

                        Dispatcher.UIThread.Post(() => MainScrollOffset = scrollOffset, DispatcherPriority.Background);
                    });
                }
            });
        }
        catch (Exception e)
        {
            logger?.LogError(e, "Failed to refresh items");
        }
    }

    public void OpenImportWindow()
    {
        windowService?.ShowSingletonWindow<ImportWindowViewModel>();
    }

    public void OpenUploadWindow()
    {
        windowService?.ShowSingletonWindow<UploadWindowViewModel>();
    }

    public void OpenSettings()
    {
        windowService?.ShowSingletonWindow<SettingsWindowViewModel>();
    }

    public void OpenRestoreDeleted()
    {
        windowService?.ShowSingletonWindow<RestoreDeletedWindowViewModel>();
    }

    public void OpenTagManager()
    {
        windowService?.ShowSingletonWindow<TagManagerWindowViewModel>();
    }

    public void OpenMaintenanceTools()
    {
        windowService?.ShowSingletonWindow<MaintenanceToolsWindowViewModel>();
    }

    public void Dispose()
    {
        if (backendStatusService != null)
        {
            backendStatusService.OnStatusChanged -= OnBackendConnectionChanged;
        }

        if (signalRService != null)
        {
            signalRService.OnMediaFoldersUpdated -= OnMediaFoldersUpdated;
            signalRService.OnMediaFolderContentsUpdated -= OnMediaFolderContentsUpdated;
        }

        if (backgroundJobs != null)
        {
            // backgroundJobs.CancelJob(RefreshBackendStatusString);
        }

        if (signalRService != null)
        {
            // signalRService.OnAIJobCreated -= OnJobCreated;
            // signalRService.OnAIJobUpdated -= CheckJobUpdate;
        }

        foreach (var task in MainItems)
        {
            task.Dispose();
        }

        Hamburger.Dispose();
    }

    private void RefreshBackendStatusString()
    {
        if (signalRService != null)
        {
            StatusText = GetBackendStatus(signalRService.IsConnected);
        }
    }

    private string GetBackendStatus(bool connected)
    {
        return connected ? "Connected to backend" : "Disconnected from backend";
    }

    private void OnBackendConnectionChanged(bool connected)
    {
        Dispatcher.UIThread.Post(() => StatusText = GetBackendStatus(connected));
    }

    private void OnMediaFoldersUpdated()
    {
        // TODO: make this only refresh when the folder is in the current folder
        _ = RefreshItems();
    }

    private void OnMediaFolderContentsUpdated(long folderId)
    {
        if (folderId == currentFolderId)
        {
            _ = RefreshItems();
        }
    }

    private void InitializeMenu()
    {
        AddDefaultMenuItems(Hamburger);

        // TODO: implement duplicate finding window (planned for later)
        /*Hamburger.MenuItems.Add(new HamburgerMenuItem
            { Title = "Find Duplicates", Command = new RelayCommand(OpenDuplicateFinder) });*/

        Hamburger.MenuItems.Add(new HamburgerMenuItem
            { Title = "Settings", Command = new RelayCommand(OpenSettings) });
        Hamburger.MenuItems.Add(new HamburgerMenuItem
            { Title = "Tag Manager", Command = new RelayCommand(OpenTagManager) });
        Hamburger.MenuItems.Add(new HamburgerMenuItem
            { Title = "Maintenance Tools", Command = new RelayCommand(OpenMaintenanceTools) });
        Hamburger.MenuItems.Add(new HamburgerMenuItem
            { Title = "Restore Deleted", Command = new RelayCommand(OpenRestoreDeleted) });

        AddTrailingMenuItems(Hamburger, windowService);
    }

    private class MainWindowMediaActions : IMediaAssociatedWindows
    {
        private readonly MainWindowViewModel viewModel;
        private readonly IWindowService windowService;

        public MainWindowMediaActions(MainWindowViewModel viewModel, IWindowService windowService)
        {
            this.viewModel = viewModel;
            this.windowService = windowService;
        }

        public IMediaAssociatedWindows.DoubleClickAction DefaultDoubleClickAction =>
            IMediaAssociatedWindows.DoubleClickAction.OpenView;

        public bool HasViewAction => true;
        public bool HasThumbnailAction => false;
        public bool HasEditAction => true;
        public bool HasMoveToFolderAction => false;
        public bool HasAddToFolderAction => false;
        public bool HasManageFoldersAction { get; private set; }

        public void RefreshAvailableOptions(IVisualMediaSource? mediaSource)
        {
            HasManageFoldersAction = mediaSource is ServerMediaSource;
        }

        public void ShowView(IVisualMediaSource? mediaSource)
        {
            if (mediaSource is ServerMediaSource serverSource)
            {
                if (serverSource.Info.IsFolder)
                {
                    viewModel.NavigateToFolder(serverSource.ServerId);
                }
                else if (serverSource.Info.IsCollection)
                {
                    viewModel.OpenCollection(serverSource.ServerId, serverSource.Info.Name);
                }
                else
                {
                    // For normal media, we want to open it in a viewer
                    windowService.ShowMediaViewer(mediaSource.Clone());
                }
            }
        }

        public void ShowThumbnail(IVisualMediaSource mediaSource)
        {
            windowService.ShowMediaViewer(mediaSource.Clone());
        }

        public void StartEditAction(IVisualMediaSource mediaSource)
        {
            if (mediaSource is ServerMediaSource serverMediaSource)
            {
                windowService.ShowMediaEditSetup(serverMediaSource.ServerId);
            }
        }

        public void StartMoveAction(IVisualMediaSource mediaSource) => throw new NotSupportedException();
        public void StartAddToFolderAction(IVisualMediaSource mediaSource) => throw new NotSupportedException();

        public void StartManageFoldersAction(IVisualMediaSource mediaSource)
        {
            if (mediaSource is ServerMediaSource serverMediaSource)
            {
                windowService.ShowEditMediaFolders(serverMediaSource.ServerId);
            }
        }
    }
}
