using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using DualView.GUI.Services;
using DualView.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DualView.GUI.ViewModels;

public sealed class ImportSectionWindowViewModel : ViewModelBase, IDisposable
{
    private readonly ILogger<ImportSectionWindowViewModel>? logger;
    private readonly IClientDatabaseService? databaseService;
    private readonly IWindowService? windowService;
    private readonly ILogger<ImportSectionViewModel>? importSectionLogger;
    private readonly ILogger<FolderPickerViewModel>? folderPickerLogger;
    private readonly ISignalRService? signalRService;
    private readonly IBackendAPI? backendAPI;
    private readonly IServiceProvider? serviceProvider;

    private long sectionId = -1;
    private bool isDisposed;

    public ImportSectionWindowViewModel()
    {
        Hamburger = new HamburgerMenuViewModel();
        Section = new ImportSectionViewModel();
        WindowTitle = "DualView - Import section";
        InitializeMenu();
    }

    [ActivatorUtilitiesConstructor]
    public ImportSectionWindowViewModel(ILogger<ImportSectionWindowViewModel> logger,
        IClientDatabaseService databaseService, IBackendStatusService backendStatusService,
        IWindowService windowService, ILogger<ImportSectionViewModel> importSectionLogger,
        ILogger<FolderPickerViewModel> folderPickerLogger,
        ISignalRService signalRService, IBackendAPI backendAPI, IServiceProvider serviceProvider)
    {
        this.logger = logger;
        this.databaseService = databaseService;
        this.windowService = windowService;
        this.importSectionLogger = importSectionLogger;
        this.folderPickerLogger = folderPickerLogger;
        this.signalRService = signalRService;
        this.backendAPI = backendAPI;
        this.serviceProvider = serviceProvider;

        Hamburger = new HamburgerMenuViewModel(backendStatusService);
        InitializeMenu();
        signalRService.OnUploadSectionsUpdated += OnUploadSectionsUpdated;
        signalRService.OnUploadSectionUpdated += OnUploadSectionUpdated;
    }

    public HamburgerMenuViewModel Hamburger { get; }

    public ImportSectionViewModel? Section
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public string WindowTitle
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    public event EventHandler? CloseRequested;

    public async Task InitializeAsync(long id)
    {
        sectionId = id;
        if (databaseService == null)
            return;

        try
        {
            var section = await databaseService.GetUploadSectionAsync(id);
            if (section == null)
            {
                Close();
                return;
            }

            var importSection = new ImportSectionViewModel(section, databaseService, importSectionLogger,
                windowService, folderPickerLogger, serviceProvider, signalRService, backendAPI);
            Section = importSection;
            WindowTitle = $"DualView - Import section: {section.Name}";
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to load import section {SectionId} in pop-out window", id);
            Close();
        }
    }

    public void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (isDisposed)
            return;

        isDisposed = true;
        if (signalRService != null)
        {
            signalRService.OnUploadSectionsUpdated -= OnUploadSectionsUpdated;
            signalRService.OnUploadSectionUpdated -= OnUploadSectionUpdated;
        }

        Section?.Dispose();
        Hamburger.Dispose();
    }

    private void InitializeMenu()
    {
        MainWindowViewModel.AddDefaultMenuItems(Hamburger);
        MainWindowViewModel.AddTrailingMenuItems(Hamburger, windowService);
    }

    private async void OnUploadSectionsUpdated()
    {
        if (databaseService == null || sectionId < 0 || isDisposed)
            return;

        try
        {
            var section = await databaseService.GetUploadSectionAsync(sectionId);
            if (section == null)
            {
                Close();
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Section?.UpdateFromServer(section);
                WindowTitle = $"DualView - Import section: {section.Name}";
            });
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to refresh import section {SectionId} in pop-out window", sectionId);
        }
    }

    private void OnUploadSectionUpdated(long updatedSectionId)
    {
        if (updatedSectionId == sectionId)
            _ = RefreshSectionAsync();
    }

    private async Task RefreshSectionAsync()
    {
        if (databaseService == null || isDisposed)
            return;

        var section = await databaseService.GetUploadSectionAsync(sectionId);
        if (section == null)
        {
            Close();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Section?.UpdateFromServer(section);
            WindowTitle = $"DualView - Import section: {section.Name}";
        });
    }
}
