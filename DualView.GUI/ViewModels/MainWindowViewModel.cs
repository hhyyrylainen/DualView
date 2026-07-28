using System;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using DualView.GUI.Services;
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

    // Default constructor for design-time
    public MainWindowViewModel()
    {
        Hamburger = new HamburgerMenuViewModel();
        InitializeMenu();
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

        Hamburger = new HamburgerMenuViewModel(backendStatusService);

        InitializeMenu();

        logger.LogInformation("MainWindowViewModel initialized");

        backendStatusService.OnStatusChanged += OnBackendConnectionChanged;
        // backgroundJobs.Schedule(RefreshBackendStatusString, TimeSpan.FromSeconds(1));

        // RefreshItems();
    }

    public string StatusText
    {
        get;
        set => SetProperty(ref field, value);
    } = "Contacting backend...";


    public HamburgerMenuViewModel Hamburger { get; }

    // TODO: items view as a flow container
    public ObservableCollection<MediaViewerViewModel> MainItems { get; } = new();

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

    public void OpenMediaManager()
    {
        windowService?.ShowSingletonWindow<MediaCollectionWindowViewModel>();
    }

    public void OpenImportWindow()
    {
        windowService?.ShowSingletonWindow<ImportWindowViewModel>();
    }

    public void OpenSettings()
    {
        windowService?.ShowSingletonWindow<SettingsWindowViewModel>();
    }

    public void OpenRestoreDeleted()
    {
        windowService?.ShowSingletonWindow<RestoreDeletedWindowViewModel>();
    }

    public void Dispose()
    {
        if (backendStatusService != null)
        {
            backendStatusService.OnStatusChanged -= OnBackendConnectionChanged;
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

    private void OnBackendConnectionChanged(bool connected)
    {
        Dispatcher.UIThread.Post(() => StatusText = connected ? "Connected to backend" : "Disconnected from backend");
    }

    private void InitializeMenu()
    {
        AddDefaultMenuItems(Hamburger);
        Hamburger.MenuItems.Add(new HamburgerMenuItem
            { Title = "Media", Command = new RelayCommand(OpenMediaManager) });
        Hamburger.MenuItems.Add(new HamburgerMenuItem
            { Title = "Import Media", Command = new RelayCommand(OpenImportWindow) });


        Hamburger.MenuItems.Add(new HamburgerMenuItem
            { Title = "Settings", Command = new RelayCommand(OpenSettings) });
        Hamburger.MenuItems.Add(new HamburgerMenuItem
            { Title = "Restore Deleted", Command = new RelayCommand(OpenRestoreDeleted) });

        AddTrailingMenuItems(Hamburger, windowService);
    }
}
