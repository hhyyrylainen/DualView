using System;
using System.Threading.Tasks;
using DualView.GUI.Services;
using DualView.Shared.Models;
using DualView.Shared.Services;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DualView.GUI.ViewModels;

public class SettingsWindowViewModel : ViewModelBase, IDisposable
{
    private readonly ILogger<SettingsWindowViewModel>? logger;
    private readonly IWindowService? windowService;
    private readonly IClientDatabaseService? databaseService;
    private readonly IBackgroundJobs? backgroundJobs;
    private readonly ISignalRService? signalRService;
    private readonly IBackendAPI? backendAPI;

    private bool unsavedMainSettings;

    private bool savingSettings;

    private DualViewSettings? generalSettings;

    public Func<string, Task>? RequestCopyToClipboard { get; set; }

    // Design time constructor
    public SettingsWindowViewModel()
    {
        Hamburger = new HamburgerMenuViewModel();

        ApplyMainSettings(null);
    }

    [ActivatorUtilitiesConstructor]
    public SettingsWindowViewModel(ILogger<SettingsWindowViewModel> logger, IWindowService windowService,
        IClientDatabaseService databaseService, IBackgroundJobs backgroundJobs, ISignalRService signalRService,
        IBackendStatusService backendStatusService, IBackendAPI backendAPI)
    {
        this.logger = logger;
        this.windowService = windowService;
        this.databaseService = databaseService;
        this.backgroundJobs = backgroundJobs;
        this.signalRService = signalRService;
        this.backendAPI = backendAPI;

        Hamburger = new HamburgerMenuViewModel(backendStatusService);

        signalRService.OnAppSettingsUpdated += TriggerLoadSettings;

        InitializeMenu();

        logger.LogInformation("SettingsWindowViewModel initialized");

        ReloadSettings();
    }

    public bool SidePanelOpen
    {
        get;
        set => SetProperty(ref field, value);
    } = true;

    public bool TabIsGeneral
    {
        get;
        set => SetProperty(ref field, value);
    } = true;

    public bool TabIsMisc
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool UnsavedMainSettings
    {
        get => unsavedMainSettings;
        set
        {
            if (value == unsavedMainSettings)
                return;

            SetProperty(ref unsavedMainSettings, value);
            OnPropertyChanged(nameof(UnsavedChanges));
        }
    }

    public bool UnsavedChanges => unsavedMainSettings;

    // General settings
    public string LocalMediaStorageLocation
    {
        get => generalSettings?.LocalMediaStorageLocation ?? string.Empty;
        set
        {
            if (generalSettings == null || generalSettings.LocalMediaStorageLocation == value)
                return;

            generalSettings.LocalMediaStorageLocation = value;
            UnsavedMainSettings = true;
            OnPropertyChanged();
        }
    }

    public string AIRunManagerUrl
    {
        get => generalSettings?.AIRunManagerUrl ?? string.Empty;
        set
        {
            if (generalSettings == null || generalSettings.AIRunManagerUrl == value)
                return;

            generalSettings.AIRunManagerUrl = value;
            UnsavedMainSettings = true;
            OnPropertyChanged();
        }
    }

    public string AIRunManagerAccessKey
    {
        get => generalSettings?.AIRunManagerAccessKey ?? string.Empty;
        set
        {
            if (generalSettings == null || generalSettings.AIRunManagerAccessKey == value)
                return;

            generalSettings.AIRunManagerAccessKey = value;
            UnsavedMainSettings = true;
            OnPropertyChanged();
        }
    }

    public string BrowserPluginAccessKey => generalSettings?.BrowserPluginAccessKey ?? string.Empty;

    public bool ShowRunManagerKey
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool ShowBrowserPluginKey
    {
        get;
        set => SetProperty(ref field, value);
    }

    public int AudioBufferingMs
    {
        get => generalSettings?.AudioBufferingMs ?? 60;
        set
        {
            if (value < 0)
                value = 0;

            if (generalSettings == null || generalSettings.AudioBufferingMs == value)
                return;

            generalSettings.AudioBufferingMs = value;
            UnsavedMainSettings = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AudioBufferingMsText));
        }
    }

    public string AudioBufferingMsText
    {
        get => AudioBufferingMs.ToString();
        set
        {
            if (int.TryParse(value, out var result))
            {
                AudioBufferingMs = result;
            }
            else
            {
                OnPropertyChanged();
            }
        }
    }

    public bool HoldPurge
    {
        get => generalSettings?.HoldPurge ?? false;
        set
        {
            if (generalSettings == null || generalSettings.HoldPurge == value)
                return;

            generalSettings.HoldPurge = value;
            UnsavedMainSettings = true;
            OnPropertyChanged();
        }
    }

    public bool UseCurlForRemoteDownloads
    {
        get => generalSettings?.UseCurlForRemoteDownloads ?? false;
        set
        {
            if (generalSettings == null || generalSettings.UseCurlForRemoteDownloads == value)
                return;

            generalSettings.UseCurlForRemoteDownloads = value;
            UnsavedMainSettings = true;
            OnPropertyChanged();
        }
    }

    // Other properties

    public HamburgerMenuViewModel Hamburger { get; }

    public void SelectGeneral()
    {
        TabIsGeneral = true;
        TabIsMisc = false;
    }

    public void SelectMisc()
    {
        TabIsMisc = true;
        TabIsGeneral = false;
    }

    public void OpenSidePanel()
    {
        SidePanelOpen = true;
    }

    public void CloseSidePanel()
    {
        SidePanelOpen = false;
    }

    public void RegenerateBrowserPluginAccessKey()
    {
        Task.Run(RegenerateBrowserPluginAccessKeyAsync);
    }

    public async Task CopyBrowserPluginAccessKey()
    {
        if (RequestCopyToClipboard == null)
            throw new InvalidOperationException("Clipboard is not available");

        await RequestCopyToClipboard(BrowserPluginAccessKey);
    }

    private async Task RegenerateBrowserPluginAccessKeyAsync()
    {
        if (backendAPI == null)
            throw new InvalidOperationException("No backend API provided");

        try
        {
            await backendAPI.RegenerateBrowserPluginAccessKey();
            await LoadMainSettings();
        }
        catch (Exception e)
        {
            logger?.LogError(e, "Failed to regenerate the browser plugin access key");
            windowService?.ShowErrorWindow("Failed to regenerate browser plugin access key", e);
        }
    }

    public async Task SaveChanges()
    {
        if (!UnsavedChanges)
        {
            logger?.LogDebug("No changes to save");
            return;
        }

        if (databaseService == null)
            throw new InvalidOperationException("No database service provided");

        logger?.LogInformation("Saving changes to settings...");

        savingSettings = true;

        if (UnsavedMainSettings && generalSettings != null)
        {
            try
            {
                await databaseService.SaveAppSettingsAsync(generalSettings);
                UnsavedMainSettings = false;
            }
            catch (Exception e)
            {
                logger?.LogError(e, "Failed to save app settings");
                windowService?.ShowErrorWindow("Failed to save settings", e);
            }
        }

        savingSettings = false;
    }

    public void ReloadSettings()
    {
        // We want all fresh data so that our local changes are forgotten
        Task.Run(LoadMainSettings);
    }

    public async Task LoadMainSettings()
    {
        if (databaseService == null)
            throw new InvalidOperationException("No database service provided");

        if (savingSettings)
        {
            logger?.LogDebug("Skipping runner settings loading while saving is in progress");
            return;
        }

        logger?.LogInformation("Loading settings to the GUI");

        try
        {
            ApplyMainSettings(await databaseService.GetAppSettingsAsync());
            UnsavedMainSettings = false;
        }
        catch (Exception e)
        {
            logger?.LogError(e, "Failed to load settings");
            windowService?.ShowErrorWindow("Failed to load settings", e);
        }

        OnPropertyChanged(nameof(LocalMediaStorageLocation));
        OnPropertyChanged(nameof(AIRunManagerAccessKey));
        OnPropertyChanged(nameof(AIRunManagerUrl));
        OnPropertyChanged(nameof(BrowserPluginAccessKey));
        OnPropertyChanged(nameof(AudioBufferingMs));
        OnPropertyChanged(nameof(AudioBufferingMsText));
        OnPropertyChanged(nameof(UseCurlForRemoteDownloads));
        OnPropertyChanged(nameof(HoldPurge));
    }

    public void Dispose()
    {
        if (signalRService != null)
        {
            signalRService.OnAppSettingsUpdated -= TriggerLoadSettings;
        }

        Hamburger.Dispose();
    }

    private void ApplyMainSettings(DualViewSettings? settings)
    {
        generalSettings = settings ?? new DualViewSettings();
    }

    private void TriggerSaveSettings()
    {
        Task.Run(SaveChanges);
    }

    private void TriggerLoadSettings()
    {
        Task.Run(LoadMainSettings);
    }

    private void InitializeMenu()
    {
        MainWindowViewModel.AddDefaultMenuItems(Hamburger);

        Hamburger.MenuItems.Add(new HamburgerMenuItem
            { Title = "Save Changes", Command = new RelayCommand(TriggerSaveSettings) });

        Hamburger.MenuItems.Add(new HamburgerMenuItem
            { Title = "Reload Settings", Command = new RelayCommand(ReloadSettings) });

        MainWindowViewModel.AddTrailingMenuItems(Hamburger, windowService);
    }
}
